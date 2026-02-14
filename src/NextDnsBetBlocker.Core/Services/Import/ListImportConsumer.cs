namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using Polly;
using System.Diagnostics;
using System.Threading.Channels;

/// <summary>
/// Consumidor de dados para Table Storage com PARALELISMO e OBSERVABILIDADE
/// Lê do channel, agrupa por partição, insere em paralelo
/// Otimizado para atingir 18k ops/s com logging detalhado de performance
/// </summary>
public class ListImportConsumer : IListImportConsumer
{
    private readonly ILogger<ListImportConsumer> _logger;
    private readonly IListTableStorageRepository _tableRepository;
    private readonly IImportMetricsCollector _metricsCollector;
    private readonly IImportRateLimiter _rateLimiter;
    private readonly IPartitionKeyStrategy _partitionKeyStrategy;
    private readonly IAsyncPolicy<BatchOperationResult> _resilientPolicy;
    private readonly ParallelImportConfig _parallelConfig;
    private PartitionRateLimiter? _partitionRateLimiter;

    public ListImportConsumer(
        ILogger<ListImportConsumer> logger,
        IListTableStorageRepository tableRepository,
        IImportMetricsCollector metricsCollector,
        IImportRateLimiter rateLimiter,
        IPartitionKeyStrategy partitionKeyStrategy,
        ParallelImportConfig? parallelConfig = null)
    {
        _logger = logger;
        _tableRepository = tableRepository;
        _metricsCollector = metricsCollector;
        _rateLimiter = rateLimiter;
        _partitionKeyStrategy = partitionKeyStrategy;
        _parallelConfig = parallelConfig ?? new ParallelImportConfig();
        _resilientPolicy = BuildResiliencePolicy();

        if (_parallelConfig.UsePartitionRateLimiting)
        {
            _partitionRateLimiter = new PartitionRateLimiter(_parallelConfig.MaxOpsPerSecondPerPartition);
        }
    }

    public async Task ConsumeAsync(
        Channel<string> inputChannel,
        ListImportConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Consumer started for {ListName} → {TableName} (MaxParallelism={MaxParallelism})",
                config.ListName,
                config.TableName,
                _parallelConfig.MaxDegreeOfParallelism);

            // Garantir que tabela existe
            await _tableRepository.EnsureTableExistsAsync(config.TableName, cancellationToken);

            // Criar gerenciador paralelo e monitor de performance
            var batchManager = new ParallelBatchManager(_parallelConfig);
            var batchManagerMetrics = batchManager.GetMetrics();
            var performanceMonitor = new PerformanceMonitor(config.MaxPartitions * 1_000_000);  // Estimativa
            var performanceLogger = new PerformanceLogger(_logger, config.ListName);
            var progressStopwatch = Stopwatch.StartNew();
            int itemCount = 0;

            try
            {
                // Fase 1: Enfileirar items (agrupados por partição)
                _logger.LogInformation("Phase 1: Queuing items from producer...");

                await foreach (var domain in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _metricsCollector.RecordItemProcessed();

                    // Criar entrada para Table Storage
                    var entry = new DomainListEntry
                    {
                        PartitionKey = _partitionKeyStrategy.GetPartitionKey(domain),
                        RowKey = domain,
                        Timestamp = DateTime.UtcNow
                    };

                    // Enfileirar no gerenciador paralelo
                    batchManager.Enqueue(entry);
                    itemCount++;
                    performanceMonitor.IncrementProcessed(1);

                    // Report progress periodicamente
                    if (itemCount % Math.Max(1000, itemCount / 100) == 0)
                    {
                        var progressStats = performanceMonitor.GetStats();
                        performanceLogger.LogProgress(progressStats, intervalMs: 5000);
                    }
                }

                _logger.LogInformation(
                    "Phase 1 completed. Queued {Count:N0} items. Starting Phase 2: Parallel flush...",
                    itemCount);

                // Log distribuição de items por partição
                var itemsDistribution = batchManagerMetrics.GetItemsDistribution();
                performanceLogger.LogLoadDistribution(itemsDistribution);

                // Verificar desbalanceamento
                if (batchManagerMetrics.HasLoadImbalance(out var percentages))
                {
                    _logger.LogWarning(
                        "[{ListName}] ⚠ Load imbalance detected: {PartitionDistribution}",
                        config.ListName,
                        string.Join(" | ", percentages.Select(kvp => $"{kvp.Key}: {kvp.Value:F1}%")));
                }

                // Log estatísticas de enfileiramento
                var (totalEnqueued, totalBatches, maxQueueDepth, backpressureEvents) = batchManagerMetrics.GetTotalMetrics();
                _logger.LogInformation(
                    "[{ListName}] Enqueueing stats: {Total} items → {Batches} batches | Max queue depth: {MaxDepth} | Backpressure events: {Events}",
                    config.ListName,
                    totalEnqueued,
                    totalBatches,
                    maxQueueDepth,
                    backpressureEvents);

                performanceMonitor = new PerformanceMonitor(itemCount);  // Reset com contagem real

                // Fase 2: Flush paralelo
                _logger.LogInformation("Phase 2: Starting parallel flush with {MaxParallelism} concurrent tasks...", _parallelConfig.MaxDegreeOfParallelism);

                await batchManager.FlushAsync(
                    async batch => await SendBatchAsync(batch, config.TableName, performanceMonitor, cancellationToken),
                    cancellationToken);

                progressStopwatch.Stop();
                var stats = performanceMonitor.GetStats();

                // Log resumo final
                performanceLogger.LogCompletionSummary(stats);

                // Log estatísticas finais de flushing
                var flushMetrics = batchManagerMetrics.GetPartitionMetrics();
                if (flushMetrics.Count > 0)
                {
                    _logger.LogInformation("[{ListName}] Flush Statistics:", config.ListName);
                    foreach (var partition in flushMetrics.OrderBy(x => x.Key))
                    {
                        _logger.LogInformation(
                            "[{ListName}]   Partition {Key}: {Batches} batches processed | Backpressure hits: {BP}",
                            config.ListName,
                            partition.Key,
                            partition.Value.BatchesCreated,
                            partition.Value.BackpressureCount);
                    }
                }

                // Log distribuição final
                var finalDistribution = batchManagerMetrics.GetItemsDistribution();
                performanceLogger.LogLoadDistribution(finalDistribution);

                // Log métricas por partição
                if (_partitionRateLimiter != null)
                {
                    var partitionStats = _partitionRateLimiter.GetAllPartitionStats();
                    var partitionSummaries = new Dictionary<string, PartitionSummary>();

                    foreach (var stat in partitionStats)
                    {
                        partitionSummaries[stat.Key] = new PartitionSummary
                        {
                            PartitionKey = stat.Key,
                            Throughput = stat.Value.CurrentOpsPerSecond,
                            TotalItems = (int)stat.Value.TotalOperations,
                            AverageLatency = stat.Value.AverageLatencyMs,
                            P99Latency = 0  // Poderia adicionar se tivesse em PartitionStats
                        };
                    }

                    performanceLogger.LogPartitionsSummary(partitionSummaries);
                }

                // Report final metrics
                var finalMetrics = _metricsCollector.GetCurrentMetrics();
                progress.Report(new ImportProgress { Metrics = finalMetrics });
            }
            finally
            {
                batchManager.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Consumer cancelled for {ListName}", config.ListName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Consumer failed for {ListName}", config.ListName);
            throw;
        }
    }

    private async Task SendBatchAsync(
        List<DomainListEntry> batch,
        string tableName,
        PerformanceMonitor performanceMonitor,
        CancellationToken cancellationToken)
    {
        if (batch == null || batch.Count == 0)
            return;

        // Rate limit por partição (se configurado)
        if (_partitionRateLimiter != null && batch.Count > 0)
        {
            var partitionKey = batch[0].PartitionKey;
            await _partitionRateLimiter.WaitAsync(partitionKey, batch.Count, cancellationToken);
        }
        else
        {
            // Fallback: usar rate limiter global
            await _rateLimiter.WaitAsync(batch.Count, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Executar com resiliência (Polly)
            var result = await _resilientPolicy.ExecuteAsync(
                async (ct) => await _tableRepository.UpsertBatchAsync(tableName, batch, ct),
                cancellationToken);

            stopwatch.Stop();

            if (result.IsSuccess)
            {
                _metricsCollector.RecordBatchSuccess(batch.Count, stopwatch.ElapsedMilliseconds);
                performanceMonitor.IncrementProcessed(batch.Count);
                performanceMonitor.RecordLatency(stopwatch.ElapsedMilliseconds);

                // Record latency por partição
                if (_partitionRateLimiter != null && batch.Count > 0)
                {
                    _partitionRateLimiter.RecordOperationLatency(batch[0].PartitionKey, stopwatch.ElapsedMilliseconds, batch.Count);
                }

                _rateLimiter.RecordOperationLatency(stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
                performanceMonitor.IncrementFailed(batch.Count);
                performanceMonitor.RecordLatency(stopwatch.ElapsedMilliseconds);

                _logger.LogWarning(
                    "Batch failed: {BatchId}, Partition: {Partition}, Errors={Errors}, Message={Message}",
                    result.BatchId,
                    batch[0].PartitionKey,
                    result.FailureCount,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
            performanceMonitor.IncrementFailed(batch.Count);
            performanceMonitor.RecordLatency(stopwatch.ElapsedMilliseconds);

            _logger.LogError(
                ex,
                "Batch execution failed for partition {Partition}",
                batch.Count > 0 ? batch[0].PartitionKey : "unknown");
            throw;
        }
    }

    /// <summary>
    /// Construir política de resiliência com Polly
    /// Retry exponencial com jitter
    /// </summary>
    private static IAsyncPolicy<BatchOperationResult> BuildResiliencePolicy()
    {
        // Retry com backoff exponencial
        var retryPolicy = Policy
            .HandleResult<BatchOperationResult>(r => !r.IsSuccess)
            .Or<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                {
                    // 2s, 4s, 8s
                    var delayMs = (int)(2000 * Math.Pow(2, attempt - 1));
                    var jitter = Random.Shared.Next(0, (int)(delayMs * 0.1)); // ±10%
                    return TimeSpan.FromMilliseconds(delayMs + jitter);
                },
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    if (outcome.Exception != null)
                    {
                        // Logging via ILogger se disponível
                    }
                });

        return retryPolicy;
    }
}

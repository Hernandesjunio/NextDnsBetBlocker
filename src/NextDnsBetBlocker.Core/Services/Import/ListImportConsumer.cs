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

            // Criar gerenciador paralelo
            var batchManagerLogger = new ParallelBatchManagerLogger(_logger);
            var batchManager = new ParallelBatchManager(_parallelConfig, batchManagerLogger);
            var batchManagerMetrics = batchManager.GetMetrics();
            var adaptiveController = new AdaptiveParallelismController(_logger, _parallelConfig.MaxDegreeOfParallelism);
            var failedBatches = new FailedBatchQueue();
            var performanceMonitor = new PerformanceMonitor(config.MaxPartitions * 1_000_000);
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

                // ✅ Phase 2: Flush será instrumentado no ParallelBatchManager
                await batchManager.FlushAsync(
                    async batch => await SendBatchAsync(batch, config.TableName, cancellationToken, adaptiveController, failedBatches),
                    cancellationToken);

                // ✅ Phase 3: Reprocessar batches falhados com paralelismo reduzido
                _logger.LogInformation("[Phase 3] Starting retry of failed batches | Queue size: {Count}", failedBatches.Count);

                int retryAttempts = 0;
                while (failedBatches.Count > 0 && retryAttempts < 5)  // Máximo 5 ciclos de retry
                {
                    retryAttempts++;
                    var failedCount = failedBatches.Count;
                    _logger.LogInformation("[Phase 3] Retry cycle {Attempt}: Processing {Count} failed batches with {Parallelism} concurrent tasks",
                        retryAttempts, failedCount, adaptiveController.GetCurrentDegreeOfParallelism());

                    var retryTasks = new List<Task>();
                    var retrySemaphore = new SemaphoreSlim(adaptiveController.GetCurrentDegreeOfParallelism());

                    while (failedBatches.TryDequeue(out var failedEntry))
                    {
                        if (failedEntry == null)
                            continue;

                        await retrySemaphore.WaitAsync(cancellationToken);

                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                await SendBatchAsync(failedEntry.Batch, config.TableName, cancellationToken, adaptiveController, null);
                                // ✅ Sucesso - não adiciona de volta
                            }
                            catch
                            {
                                // Falhou novamente - adicionar de volta à fila
                                failedBatches.Enqueue(failedEntry.Batch, failedEntry.PartitionKey, $"Retry attempt {failedEntry.AttemptCount + 1}");
                                failedBatches.RecordRetry(failedEntry, "Retry failed");
                            }
                            finally
                            {
                                retrySemaphore.Release();
                            }
                        }, cancellationToken);

                        retryTasks.Add(task);
                    }

                    if (retryTasks.Count > 0)
                    {
                        await Task.WhenAll(retryTasks);
                    }

                    retrySemaphore.Dispose();

                    // Aguardar um pouco antes do próximo ciclo
                    if (failedBatches.Count > 0)
                    {
                        await Task.Delay(5000, cancellationToken);
                    }
                }

                // Log final de retry
                var (totalFailed, totalRetried, remaining) = failedBatches.GetStats();
                if (remaining > 0)
                {
                    _logger.LogError("[Phase 3] ⚠ {Remaining} batches still failed after 5 retry cycles", remaining);
                }
                else
                {
                    _logger.LogInformation("[Phase 3] ✓ All failed batches successfully reprocessed!");
                }

                var (timeouts, successes, current, initial) = adaptiveController.GetStats();
                _logger.LogInformation("[Adaptive] Final stats: {Timeouts} timeouts detected | Parallelism adjusted: {Initial} → {Current} tasks",
                    timeouts, initial, current);

                // ✅ Log estatísticas finais (apenas PartitionRateLimiter metrics)
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
                            P99Latency = 0
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
        CancellationToken cancellationToken,
        AdaptiveParallelismController adaptiveController = null,
        FailedBatchQueue failedBatches = null)
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
                adaptiveController?.RecordSuccess();

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
                _logger.LogWarning(
                    "Batch failed: {BatchId}, Partition: {Partition}, Errors={Errors}, Message={Message}",
                    result.BatchId,
                    batch[0].PartitionKey,
                    result.FailureCount,
                    result.ErrorMessage);

                // Adicionar à fila de retry
                failedBatches?.Enqueue(batch, batch[0].PartitionKey, result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);

            // Detectar timeout e registrar
            if (ex.Message.Contains("OperationTimedOut") || ex.InnerException?.Message.Contains("OperationTimedOut") == true)
            {
                adaptiveController?.RecordTimeout();
                _logger.LogWarning(ex, "Batch timeout detected - adding to retry queue");
            }

            _logger.LogError(
                ex,
                "Batch execution failed for partition {Partition}",
                batch.Count > 0 ? batch[0].PartitionKey : "unknown");

            // Adicionar à fila de retry
            failedBatches?.Enqueue(batch, batch.Count > 0 ? batch[0].PartitionKey : "unknown", ex.Message);

            // ❌ NÃO relançar - deixar com fila de retry
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

namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using Polly;
using System.Diagnostics;
using System.Threading.Channels;

/// <summary>
/// Consumidor de dados para Table Storage com PARALELISMO
/// Lê do channel, agrupa por partição e insere em paralelo
/// Otimizado para atingir 18k ops/s usando paralelismo distribuído
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
            var batchManager = new ParallelBatchManager(_parallelConfig);
            var progressStopwatch = Stopwatch.StartNew();
            int itemCount = 0;

            try
            {
                // Fase 1: Enfileirar items (agrupados por partição)
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

                    // Report progress periodicamente
                    if (itemCount % 10000 == 0)
                    {
                        _logger.LogInformation(
                            "Queued {Count} items, Active tasks: {ActiveTasks}",
                            itemCount,
                            batchManager.GetActiveTaskCount());
                    }
                }

                _logger.LogInformation(
                    "Producer finished. Queued {Count} items. Starting parallel flush...",
                    itemCount);

                // Fase 2: Flush paralelo
                await batchManager.FlushAsync(
                    async batch => await SendBatchAsync(batch, config.TableName, cancellationToken),
                    cancellationToken);

                var finalMetrics = _metricsCollector.GetCurrentMetrics();
                progressStopwatch.Stop();

                _logger.LogInformation(
                    "Consumer completed for {ListName}: Processed={Processed}, Inserted={Inserted}, Errors={Errors}, Time={Time}, Throughput={Throughput:F0} ops/s",
                    config.ListName,
                    finalMetrics.TotalProcessed,
                    finalMetrics.TotalInserted,
                    finalMetrics.TotalErrors,
                    finalMetrics.ElapsedTime,
                    finalMetrics.TotalProcessed / progressStopwatch.Elapsed.TotalSeconds);

                // Log métricas por partição
                if (_partitionRateLimiter != null)
                {
                    var partitionStats = _partitionRateLimiter.GetAllPartitionStats();
                    foreach (var stat in partitionStats)
                    {
                        _logger.LogInformation(
                            "Partition {PartitionKey}: {OpsPerSec:F0} ops/s, Total: {Total}, AvgLatency: {LatencyMs:F2}ms",
                            stat.Key,
                            stat.Value.CurrentOpsPerSecond,
                            stat.Value.TotalOperations,
                            stat.Value.AverageLatencyMs);
                    }
                }

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
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
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

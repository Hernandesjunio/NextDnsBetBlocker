namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using Polly;
using Polly.CircuitBreaker;
using System.Diagnostics;
using System.Threading.Channels;

/// <summary>
/// Consumidor de dados para Table Storage
/// Lê do channel, faz batch e insere com resiliência
/// Aplica rate limiting e coleta métricas
/// </summary>
public class ListImportConsumer : IListImportConsumer
{
    private readonly ILogger<ListImportConsumer> _logger;
    private readonly IListTableStorageRepository _tableRepository;
    private readonly IImportMetricsCollector _metricsCollector;
    private readonly IImportRateLimiter _rateLimiter;
    private readonly IPartitionKeyStrategy _partitionKeyStrategy;
    private readonly IAsyncPolicy<BatchOperationResult> _resilientPolicy;

    public ListImportConsumer(
        ILogger<ListImportConsumer> logger,
        IListTableStorageRepository tableRepository,
        IImportMetricsCollector metricsCollector,
        IImportRateLimiter rateLimiter,
        IPartitionKeyStrategy partitionKeyStrategy)
    {
        _logger = logger;
        _tableRepository = tableRepository;
        _metricsCollector = metricsCollector;
        _rateLimiter = rateLimiter;
        _partitionKeyStrategy = partitionKeyStrategy;
        _resilientPolicy = BuildResiliencePolicy();
    }

    public async Task ConsumeAsync(
        Channel<string> inputChannel,
        ListImportConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Consumer started for {ListName} → {TableName}", config.ListName, config.TableName);

            // Garantir que tabela existe
            await _tableRepository.EnsureTableExistsAsync(config.TableName, cancellationToken);

            var batch = new List<DomainListEntry>(config.BatchSize);
            var batchStopwatch = Stopwatch.StartNew();
            int batchCount = 0;

            // Consumir do channel
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

                batch.Add(entry);

                // Se batch completo, enviar
                if (batch.Count >= config.BatchSize)
                {
                    batchStopwatch.Restart();
                    await SendBatchAsync(batch, config.TableName, cancellationToken);
                    batchCount++;

                    // Report progress a cada 10 batches
                    if (batchCount % 10 == 0)
                    {
                        var metrics = _metricsCollector.GetCurrentMetrics();
                        var progressReport = new ImportProgress { Metrics = metrics };
                        progress.Report(progressReport);

                        _logger.LogInformation(
                            "Progress: {Processed}/{Estimated} items, {ItemsPerSec:F2} items/s, Errors: {Errors}",
                            metrics.TotalProcessed,
                            metrics.EstimatedTotalItems,
                            metrics.ItemsPerSecond,
                            metrics.TotalErrors);
                    }

                    batch.Clear();
                }
            }

            // Enviar batch final
            if (batch.Count > 0)
            {
                batchStopwatch.Restart();
                await SendBatchAsync(batch, config.TableName, cancellationToken);
                batchCount++;
            }

            var finalMetrics = _metricsCollector.GetCurrentMetrics();
            _logger.LogInformation(
                "Consumer completed for {ListName}: Processed={Processed}, Inserted={Inserted}, Errors={Errors}, Time={Time}",
                config.ListName,
                finalMetrics.TotalProcessed,
                finalMetrics.TotalInserted,
                finalMetrics.TotalErrors,
                finalMetrics.ElapsedTime);

            progress.Report(new ImportProgress { Metrics = finalMetrics });
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
        // Aplicar rate limit
        await _rateLimiter.WaitAsync(batch.Count, cancellationToken);

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
                _rateLimiter.RecordOperationLatency(stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
                _logger.LogWarning(
                    "Batch failed: {BatchId}, Errors={Errors}, Message={Message}",
                    result.BatchId,
                    result.FailureCount,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
            _logger.LogError(ex, "Batch execution failed");
            throw;
        }
    }

    /// <summary>
    /// Construir política de resiliência com Polly
    /// Retry exponencial simples
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
                        Console.WriteLine($"Retry {retryCount} after {timespan.TotalSeconds}s due to: {outcome.Exception.Message}");
                });

        return retryPolicy;
    }
}

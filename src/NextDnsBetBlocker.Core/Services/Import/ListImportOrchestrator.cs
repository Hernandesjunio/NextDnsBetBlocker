namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using Polly;
using System.Diagnostics;

/// <summary>
/// Orquestrador da importação com ShardingProcessor
/// Coordena inserção/deleção em paralelo de domínios já baixados
/// Usa ShardingProcessor para rate limiting hierárquico e degradação adaptativa
/// 
/// Fluxo:
/// 1. Recebe domínios já baixados (GenericListImporter faz download)
/// 2. Cria Entity com PartitionKey (compatível com ShardingProcessor)
/// 3. ShardingProcessor: batches + múltiplos flush workers por partição
/// 4. Executa operação apropriada (Add/Remove) com Polly para resiliência
/// 5. Retorna métricas consolidadas
/// </summary>
public class ListImportOrchestrator : IListImportOrchestrator
{
    private readonly ILogger<ListImportOrchestrator> _logger;
    private readonly IListTableStorageRepository _tableRepository;
    private readonly IImportMetricsCollector _metricsCollector;
    private readonly IPartitionKeyStrategy _partitionKeyStrategy;
    private readonly IProgressReporter _progressReporter;
    private readonly IAsyncPolicy<BatchOperationResult> _resilientPolicy;

    public ListImportOrchestrator(
        ILogger<ListImportOrchestrator> logger,
        IListTableStorageRepository tableRepository,
        IImportMetricsCollector metricsCollector,
        IImportRateLimiter rateLimiter,
        IPartitionKeyStrategy partitionKeyStrategy,
        IProgressReporter progressReporter,
        ParallelImportConfig parallelConfig)
    {
        _logger = logger;
        _tableRepository = tableRepository;
        _metricsCollector = metricsCollector;
        _partitionKeyStrategy = partitionKeyStrategy;
        _progressReporter = progressReporter;
        _resilientPolicy = BuildResiliencePolicy();
    }

    /// <summary>
    /// Executa operação de importação com domínios já baixados
    /// Usa ShardingProcessor para distribuição paralela com rate limiting hierárquico
    /// </summary>
    public async Task<ImportMetrics> ExecuteImportAsync(
        ListImportItemConfig config,
        ImportOperationType operationType,
        IEnumerable<string> domains,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        var overallStopwatch = Stopwatch.StartNew();
        int itemCount = 0;

        try
        {
            _logger.LogInformation(
                "Starting import orchestration for {ListName} into {TableName} | Operation: {OperationType}",
                config.ListName,
                config.TableName,
                operationType);

            _metricsCollector.Reset();

            // Garantir que tabela existe
            await _tableRepository.EnsureTableExistsAsync(config.TableName, cancellationToken);

            // Configuração de throttling (máximo do Table Storage)
            var throttlingConfig = new ThrottlingConfig(
                GlobalLimitPerSecond: 10000,        // Limite global de todas as operações
                PartitionLimitPerSecond: 1000);     // Limite por partição

            // Configuração de processamento
            var processingConfig = new PartitionProcessingConfig(
                BatchSize: 100,                      // Batches de 100 itens
                FlushWorkerCount: 20);               // 20 workers simultâneos por partição

            // Configuração de degradação (opcional, para resiliência)
            var degradationConfig = new AdaptiveDegradationConfig(
                Enabled: true,
                DegradationPercentagePerError: 10,
                MinimumDegradationPercentage: 80,
                RecoveryIntervalSeconds: 60,
                CircuitBreakerResetIntervalSeconds: 300);

            // Função de armazenamento (Add ou Remove)
            BatchStorageOperation storageOperation = operationType == ImportOperationType.Add
                ? (async (pk, batch) => await ExecuteAddBatchWithResilienceAsync(config.TableName, batch, cancellationToken))
                : (async (pk, batch) => await ExecuteRemoveBatchWithResilienceAsync(config.TableName, batch, cancellationToken));

            // Criar processor
            var shardingProcessor = new ShardingProcessor(
                throttlingConfig,
                processingConfig,
                storageOperation,
                _progressReporter,
                degradationConfig);

            // Converter domínios para Entity
            var entities = domains.Select(domain =>
            {
                _metricsCollector.RecordItemProcessed();
                return new Entity
                {
                    PartitionKey = _partitionKeyStrategy.GetPartitionKey(domain),
                    RowKey = domain
                };
            }).ToList();

            itemCount = entities.Count;

            // Processar com ShardingProcessor (batching + throttling + degradação adaptativa + progresso)
            await shardingProcessor.ProcessAsync(entities);

            // Obter métricas do ShardingProcessor
            var processorMetrics = shardingProcessor.GetMetrics();

            overallStopwatch.Stop();

            var finalMetrics = _metricsCollector.GetCurrentMetrics();
            finalMetrics.Status = ImportStatus.Completed;

            // Log consolidado de métricas
            _logger.LogInformation(
                "✓ Import orchestration completed for {ListName} | Total: {Processed:N0} | Inserted: {Inserted:N0} | Errors: {Errors} | Time: {Time} | Throughput: {Throughput:F0} ops/s | Success Rate: {SuccessRate}%",
                config.ListName,
                finalMetrics.TotalProcessed,
                finalMetrics.TotalInserted,
                finalMetrics.TotalErrors,
                FormatTimeSpan(overallStopwatch.Elapsed),
                itemCount > 0 ? itemCount / overallStopwatch.Elapsed.TotalSeconds : 0,
                processorMetrics.GlobalSuccessRate);

            // Log de degradação e circuit breaker se houve
            if (processorMetrics.TotalDegradationEvents > 0 || processorMetrics.TotalCircuitBreakerOpenings > 0)
            {
                _logger.LogWarning(
                    "Import completed with degradation | Degradation events: {DegradationEvents} | Circuit breaker openings: {CircuitBreakerOpenings} | Partitions affected: {PartitionsWithIssues}",
                    processorMetrics.TotalDegradationEvents,
                    processorMetrics.TotalCircuitBreakerOpenings,
                    processorMetrics.PartitionMetrics.Count(p => p.Value.DegradationCount > 0 || p.Value.IsCircuitBreakerOpen));

                foreach (var partition in processorMetrics.PartitionMetrics.Where(p => p.Value.DegradationCount > 0 || p.Value.IsCircuitBreakerOpen))
                {
                    _logger.LogInformation(
                        "Partition {PartitionKey}: Degradation={DegradationCount} | CircuitBreaker={IsOpen} | SuccessRate={SuccessRate}%",
                        partition.Key,
                        partition.Value.DegradationCount,
                        partition.Value.IsCircuitBreakerOpen ? "OPEN" : "CLOSED",
                        partition.Value.SuccessRate);
                }
            }

            progress.Report(new ImportProgress { Metrics = finalMetrics });
            return finalMetrics;
        }
        catch (OperationCanceledException)
        {
            overallStopwatch.Stop();
            _logger.LogInformation(
                "Import orchestration cancelled for {ListName} | Time: {Time} | Processed: {Count:N0} items",
                config.ListName,
                FormatTimeSpan(overallStopwatch.Elapsed),
                itemCount);
            throw;
        }
        catch (Exception ex)
        {
            overallStopwatch.Stop();
            _logger.LogError(
                ex,
                "❌ Import orchestration FAILED for {ListName} | Time: {Time} | Processed: {Count:N0} items | Error: {Error}",
                config.ListName,
                FormatTimeSpan(overallStopwatch.Elapsed),
                itemCount,
                ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Método obsoleto - mantido para compatibilidade (será removido depois)
    /// </summary>
    [Obsolete("Use ExecuteImportAsync(config, operationType, domains, progress, cancellationToken) instead", true)]
    public async Task<ImportMetrics> ExecuteImportAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Use the new overload with operationType and domains parameters");
    }

    /// <summary>
    /// Executa operação Add com resiliência (Polly)
    /// Converte Entity para DomainListEntry para compatibilidade com repositório
    /// </summary>
    private async Task ExecuteAddBatchWithResilienceAsync(
        string tableName,
        List<Entity> batch,
        CancellationToken cancellationToken)
    {
        if (batch is null || batch.Count == 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Converter Entity para DomainListEntry para Table Storage
            var domainEntries = batch.Select(e => new DomainListEntry
            {
                PartitionKey = e.PartitionKey,
                RowKey = e.RowKey,
                Timestamp = DateTime.UtcNow
            }).ToList();

            var result = await _resilientPolicy.ExecuteAsync(
                async (ct) => await _tableRepository.UpsertBatchAsync(tableName, domainEntries, ct),
                cancellationToken);

            stopwatch.Stop();

            if (result.IsSuccess)
            {
                _metricsCollector.RecordBatchSuccess(batch.Count, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
                _logger.LogWarning(
                    "Batch Add failed: {BatchId} | Partition: {Partition} | Errors: {Errors} | Message: {Message}",
                    result.BatchId,
                    batch.Count > 0 ? batch[0].PartitionKey : "unknown",
                    result.FailureCount,
                    result.ErrorMessage);
                throw new InvalidOperationException($"Batch failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
            _logger.LogError(
                ex,
                "Batch Add execution failed for partition {Partition}",
                batch.Count > 0 ? batch[0].PartitionKey : "unknown");
            throw;
        }
    }

    /// <summary>
    /// Executa operação Remove com resiliência (Polly)
    /// Converte Entity para DomainListEntry para compatibilidade com repositório
    /// </summary>
    private async Task ExecuteRemoveBatchWithResilienceAsync(
        string tableName,
        List<Entity> batch,
        CancellationToken cancellationToken)
    {
        if (batch is null || batch.Count == 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Converter Entity para DomainListEntry para Table Storage
            var domainEntries = batch.Select(e => new DomainListEntry
            {
                PartitionKey = e.PartitionKey,
                RowKey = e.RowKey,
                Timestamp = DateTime.UtcNow
            }).ToList();

            var result = await _resilientPolicy.ExecuteAsync(
                async (ct) => await _tableRepository.DeleteBatchAsync(tableName, domainEntries, ct),
                cancellationToken);

            stopwatch.Stop();

            if (result.IsSuccess)
            {
                _metricsCollector.RecordBatchSuccess(batch.Count, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
                _logger.LogWarning(
                    "Batch Remove failed: {BatchId} | Partition: {Partition} | Errors: {Errors} | Message: {Message}",
                    result.BatchId,
                    batch.Count > 0 ? batch[0].PartitionKey : "unknown",
                    result.FailureCount,
                    result.ErrorMessage);
                throw new InvalidOperationException($"Batch failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
            _logger.LogError(
                ex,
                "Batch Remove execution failed for partition {Partition}",
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
                });

        return retryPolicy;
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        else if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        else
            return $"{ts.Seconds}s";
    }
}

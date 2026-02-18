namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using Polly;
using System.Diagnostics;

/// <summary>
/// Orquestrador da importação - versão simplificada
/// Coordena inserção/deleção em paralelo de domínios já baixados
/// Não mais usa Producer/Consumer - integra toda a lógica aqui
/// 
/// Fluxo:
/// 1. Recebe domínios já baixados (GenericListImporter faz download)
/// 2. Cria DomainListEntry com PartitionKey
/// 3. Enfileira em ParallelBatchManager
/// 4. Executa Flush com operação apropriada (Add/Remove)
/// 5. Retorna métricas consolidadas
/// </summary>
public class ListImportOrchestrator : IListImportOrchestrator
{
    private readonly ILogger<ListImportOrchestrator> _logger;
    private readonly IListTableStorageRepository _tableRepository;
    private readonly IImportMetricsCollector _metricsCollector;
    private readonly IImportRateLimiter _rateLimiter;
    private readonly IPartitionKeyStrategy _partitionKeyStrategy;
    private readonly IAsyncPolicy<BatchOperationResult> _resilientPolicy;
    private readonly ParallelImportConfig _parallelConfig;

    public ListImportOrchestrator(
        ILogger<ListImportOrchestrator> logger,
        IListTableStorageRepository tableRepository,
        IImportMetricsCollector metricsCollector,
        IImportRateLimiter rateLimiter,
        IPartitionKeyStrategy partitionKeyStrategy,
        ParallelImportConfig parallelConfig)
    {
        _logger = logger;
        _tableRepository = tableRepository;
        _metricsCollector = metricsCollector;
        _rateLimiter = rateLimiter;
        _partitionKeyStrategy = partitionKeyStrategy;
        _parallelConfig = parallelConfig ?? new ParallelImportConfig();
        _resilientPolicy = BuildResiliencePolicy();
    }

    /// <summary>
    /// Executa operação de importação com domínios já baixados
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
                "Starting import orchestration for {ListName} into {TableName} | Operation: {OperationType} | MaxParallelism: {MaxParallelism}",
                config.ListName,
                config.TableName,
                operationType,
                _parallelConfig.MaxDegreeOfParallelism);

            _metricsCollector.Reset();

            // Garantir que tabela existe
            await _tableRepository.EnsureTableExistsAsync(config.TableName, cancellationToken);

            // Criar gerenciador paralelo
            var batchManager = new ParallelBatchManager(_parallelConfig, new ParallelBatchManagerLogger(_logger));

            try
            {
                // Criar entries lazy a partir dos domínios
                var entries = domains.Select(domain =>
                {
                    _metricsCollector.RecordItemProcessed();
                    return new DomainListEntry
                    {
                        PartitionKey = _partitionKeyStrategy.GetPartitionKey(domain),
                        RowKey = domain,
                        Timestamp = DateTime.UtcNow
                    };
                });

                var sendBatchFunc = operationType == ImportOperationType.Add
                    ? (Func<List<DomainListEntry>, Task>)(batch => SendBatchAsync(batch, config.TableName, ImportOperationType.Add, cancellationToken))
                    : (batch => SendBatchAsync(batch, config.TableName, ImportOperationType.Remove, cancellationToken));

                // Producer e consumers rodam em paralelo — sem deadlock
                await batchManager.ProduceAndConsumeAsync(entries, sendBatchFunc, cancellationToken);

                itemCount = batchManager.GetTotalQueuedItems();
                overallStopwatch.Stop();

                var finalMetrics = _metricsCollector.GetCurrentMetrics();
                finalMetrics.Status = ImportStatus.Completed;

                _logger.LogInformation(
                    "✓ Import orchestration completed for {ListName} | Total: {Processed:N0} | Inserted: {Inserted:N0} | Errors: {Errors} | Time: {Time} | Throughput: {Throughput:F0} ops/s",
                    config.ListName,
                    finalMetrics.TotalProcessed,
                    finalMetrics.TotalInserted,
                    finalMetrics.TotalErrors,
                    FormatTimeSpan(overallStopwatch.Elapsed),
                    itemCount > 0 ? itemCount / overallStopwatch.Elapsed.TotalSeconds : 0);

                progress.Report(new ImportProgress { Metrics = finalMetrics });
                return finalMetrics;
            }
            finally
            {
                await batchManager.DisposeAsync();
            }
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
    /// Enviar um batch para Table Storage (Add ou Remove)
    /// Rate limiting é controlado pelo ParallelBatchManager (per-partition + global)
    /// Polly cuida de retries rápidos para erros transientes
    /// </summary>
    private async Task SendBatchAsync(
        List<DomainListEntry> batch,
        string tableName,
        ImportOperationType operationType,
        CancellationToken cancellationToken)
    {
        if (batch is null || batch.Count == 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            BatchOperationResult result;

            // Executar operação apropriada com resiliência (Polly)
            if (operationType == ImportOperationType.Add)
            {
                result = await _resilientPolicy.ExecuteAsync(
                    async (ct) => await _tableRepository.UpsertBatchAsync(tableName, batch, ct),
                    cancellationToken);
            }
            else // Remove
            {
                result = await _resilientPolicy.ExecuteAsync(
                    async (ct) => await _tableRepository.DeleteBatchAsync(tableName, batch, ct),
                    cancellationToken);
            }

            stopwatch.Stop();

            if (result.IsSuccess)
            {
                _metricsCollector.RecordBatchSuccess(batch.Count, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _metricsCollector.RecordBatchFailure(batch.Count, stopwatch.ElapsedMilliseconds);
                _logger.LogWarning(
                    "Batch {Operation} failed: {BatchId} | Partition: {Partition} | Errors: {Errors} | Message: {Message}",
                    operationType,
                    result.BatchId,
                    batch.Count > 0 ? batch[0].PartitionKey : "unknown",
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
                "Batch {Operation} execution failed for partition {Partition}",
                operationType,
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

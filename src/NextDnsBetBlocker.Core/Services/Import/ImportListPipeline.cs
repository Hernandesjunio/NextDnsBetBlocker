namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Coordena pipeline sequencial de importação
/// Executa uma única vez: Hagezi → Tranco → Encerra
/// Usado em Console App rodando via ACI
/// 
/// Lógica de Decisão:
/// - Primeira vez (sem arquivo no blob): Full Import (ImportAsync)
/// - Subsequentes (arquivo existe): Diff Import (ImportDiffAsync) - otimizado 95% menos I/O
/// </summary>
public class ImportListPipeline
{
    private readonly ILogger<ImportListPipeline> _logger;
    private readonly IEnumerable<ListImportItemConfig> _configs;
    private readonly IListImporter listImporter;
    private readonly IListBlobRepository blobRepository;

    public ImportListPipeline(
        ILogger<ImportListPipeline> logger,
        IEnumerable<ListImportItemConfig> configs,
        IListImporter listImporter,
        IListBlobRepository blobRepository)
    {
        _logger = logger;
        _configs = configs;
        this.listImporter = listImporter;
        this.blobRepository = blobRepository;
    }

    /// <summary>
    /// Executa pipeline sequencial: Hagezi → Tranco
    /// Roda uma única vez e retorna resultado
    /// </summary>
    public async Task<PipelineResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var result = new PipelineResult();

        try
        {
            _logger.LogInformation(
                "╔════════════════════════════════════════╗");
            _logger.LogInformation(
                "║    Starting Import Pipeline (ACI)      ║");
            _logger.LogInformation(
                "╚════════════════════════════════════════╝");
                        
            foreach (var config in _configs)
            {
                var listName = config.ListName;

                _logger.LogInformation(
                    "\n┌─────────────────────────────────────┐");
                _logger.LogInformation(
                    "│ Importing {ListName,30} │", listName);
                _logger.LogInformation(
                    "└─────────────────────────────────────┘");

                var listResult = await ImportListAsync(config, cancellationToken);
                result.AddListResult(listName, listResult);

                _logger.LogInformation(
                    "✓ {ListName} import completed | Inserted: {Inserted} | Errors: {Errors} | Time: {Time}",
                    listName,
                    listResult.Metrics.TotalInserted,
                    listResult.Metrics.TotalErrors,
                    listResult.Metrics.ElapsedTime);
            }

            result.Success = true;

            _logger.LogInformation(
                "\n╔════════════════════════════════════════╗");
            _logger.LogInformation(
                "║   Pipeline Completed Successfully     ║");
            _logger.LogInformation(
                "║   Total Lists: {Count,25} ║", result.ListResults.Count);
            _logger.LogInformation(
                "║   Total Time: {Time,30} ║", FormatTimeSpan(result.TotalDuration));
            _logger.LogInformation(
                "╚════════════════════════════════════════╝");

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Pipeline cancelled");
            result.Success = false;
            result.CancelledMessage = "Pipeline was cancelled";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed with error");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Importar uma lista específica com detecção automática: Full ou Diff
    /// 
    /// Lógica:
    /// 1. Verifica se metadata anterior existe no blob
    /// 2. Se NÃO existe → Full Import (primeira vez)
    /// 3. Se EXISTE → Diff Import (otimizado, -95% I/O)
    /// </summary>
    private async Task<ListImportResult> ImportListAsync(
        ListImportItemConfig config,
        CancellationToken cancellationToken)
    {
        var result = new ListImportResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var now = DateTime.UtcNow;
            _logger.LogInformation(
                "Import for {ListName} started at {Time}",
                config.ListName,
                now);

            // Verificar se já existe importação anterior
            var hasMetadata = await CheckIfMetadataExistsAsync(config, cancellationToken);
            var progressReporter = CreateProgressReporter(config.ListName);

            if (!hasMetadata)
            {
                // ✅ PRIMEIRA VEZ: Full Import
                _logger.LogInformation(
                    "No previous import found for {ListName} - Performing FULL import",
                    config.ListName);

                result.Metrics = await listImporter.ImportAsync(
                    config,
                    progressReporter,
                    cancellationToken);

                result.ImportType = "Full";

                _logger.LogInformation(
                    "✓ FULL import completed for {ListName}: {Count:N0} inserted",
                    config.ListName,
                    result.Metrics.TotalInserted);
            }
            else
            {
                // ✅ SUBSEQUENTES: Diff Import (Otimizado)
                _logger.LogInformation(
                    "Previous import found for {ListName} - Performing DIFF import (optimized)",
                    config.ListName);

                result.Metrics = await listImporter.ImportDiffAsync(
                    config,
                    progressReporter,
                    cancellationToken);

                result.ImportType = "Diff";

                _logger.LogInformation(
                    "✓ DIFF import completed for {ListName}: {Inserted:N0} inserted (optimized)",
                    config.ListName,
                    result.Metrics.TotalInserted);
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import {ListName}", config.ListName);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    /// <summary>
    /// Verificar se já existe metadata (arquivo anterior) para a lista no blob storage
    /// Indica se é primeira importação (false) ou não (true)
    /// </summary>
    private async Task<bool> CheckIfMetadataExistsAsync(
        ListImportItemConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadataName = $"{config.ListName.ToLowerInvariant()}/metadata.json";
            var metadata = await blobRepository.GetImportMetadataAsync(
                config.BlobContainer,
                metadataName,
                cancellationToken);

            return metadata != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error checking metadata for {ListName} - treating as first import",
                config.ListName);
            return false; // Em caso de erro, tratar como primeira importação
        }
    }

    private string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        else if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        else
            return $"{ts.Seconds}s";
    }

    /// <summary>
    /// Cria um reporter de progresso que loga atualizações durante a importação
    /// </summary>
    private IProgress<ImportProgress> CreateProgressReporter(string listName)
    {
        return new Progress<ImportProgress>(progress =>
        {
            if (progress?.Metrics != null)
            {
                _logger.LogInformation(
                    "[{ListName}] Progress: {Inserted} inserted | {Errors} errors | {Processed} processed",
                    listName,
                    progress.Metrics.TotalInserted,
                    progress.Metrics.TotalErrors,
                    progress.Metrics.TotalProcessed);
            }
        });
    }
}

/// <summary>
/// Resultado da execução do pipeline
/// </summary>
public class PipelineResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CancelledMessage { get; set; }
    public Dictionary<string, ListImportResult> ListResults { get; } = new();

    public TimeSpan TotalDuration => 
        new TimeSpan(ListResults.Values.Sum(r => r.Duration.Ticks));

    public void AddListResult(string listName, ListImportResult result)
    {
        ListResults[listName] = result;
    }
}

/// <summary>
/// Resultado da importação de uma lista
/// </summary>
public class ListImportResult
{
    public bool Success { get; set; }
    public string ImportType { get; set; } = "Unknown";
    public ImportMetrics Metrics { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
}

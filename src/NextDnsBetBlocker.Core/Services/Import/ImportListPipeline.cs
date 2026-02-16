namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Coordena pipeline sequencial de importação
/// Executa uma única vez: Hagezi → Tranco → Encerra
/// Usado em Console App rodando via ACI
/// </summary>
public class ImportListPipeline
{
    private readonly ILogger<ImportListPipeline> _logger;
    private readonly IEnumerable<ListImportConfig> _configs;
    private readonly IListImporter listImporter;

    public ImportListPipeline(
        ILogger<ImportListPipeline> logger,
        IEnumerable<ListImportConfig> configs,
        IListImporter listImporter)
    {
        _logger = logger;
        _configs = configs;
        this.listImporter = listImporter;
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

            var orderedConfigs = new[] { "Hagezi", "TrancoList" };

            foreach (var listName in orderedConfigs)
            {
                var config = _configs.FirstOrDefault(c => 
                    c.ListName.Equals(listName, StringComparison.OrdinalIgnoreCase));

                if (config == null)
                {
                    _logger.LogWarning("Config not found for {ListName}, skipping", listName);
                    continue;
                }

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
    /// Importar uma lista específica com checkpoint tracking
    /// </summary>
    private async Task<ListImportResult> ImportListAsync(
        ListImportConfig config,
        CancellationToken cancellationToken)
    {
        var result = new ListImportResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {            
            // Atualizar checkpoint após sucesso
            // (Apenas logging, checkpoint é feito no Table Storage pelos importers)
            var now = DateTime.UtcNow;
            _logger.LogInformation(
                "Import for {ListName} started at {Time}",
                config.ListName,
                now);

            // Decidir: full import ou diff (por simplicidade, sempre full por enquanto)
            _logger.LogInformation("Performing FULL import for {ListName}", config.ListName);
            var progressReporter = CreateProgressReporter(config.ListName);
            result.Metrics = await listImporter.ImportAsync(config, progressReporter, cancellationToken);
            result.ImportType = "Full";

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

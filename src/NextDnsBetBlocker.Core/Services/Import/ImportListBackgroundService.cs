namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// BackgroundService para importação automática de listas de domínios
/// Executa importação em schedule (configurável via cron)
/// Suporta múltiplas listas (Tranco, Hagezi, etc)
/// </summary>
public class ImportListBackgroundService : BackgroundService
{
    private readonly ILogger<ImportListBackgroundService> _logger;
    private readonly IListImporter _importer;
    private readonly ListImportConfig _config;
    private CancellationTokenSource? _stoppingCts;

    public ImportListBackgroundService(
        ILogger<ImportListBackgroundService> logger,
        IListImporter importer,
        ListImportConfig config)
    {
        _logger = logger;
        _importer = importer;
        _config = config;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting ImportListBackgroundService for {ListName}",
            _config.ListName);

        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Stopping ImportListBackgroundService for {ListName}",
            _config.ListName);

        _stoppingCts?.Cancel();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Executa a importação em background
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ImportListBackgroundService executing for {ListName}",
            _config.ListName);

        // Importação inicial na startup
        try
        {
            _logger.LogInformation(
                "Performing initial import for {ListName}",
                _config.ListName);

            var progress = new Progress<ImportProgress>(ReportProgress);
            var metrics = await _importer.ImportAsync(_config, progress, cancellationToken);

            _logger.LogInformation(
                "Initial import completed for {ListName}: {Inserted} items inserted, {Errors} errors, Time: {Time}",
                _config.ListName,
                metrics.TotalInserted,
                metrics.TotalErrors,
                metrics.ElapsedTime);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Initial import cancelled for {ListName}", _config.ListName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial import failed for {ListName}", _config.ListName);
            // Não lançar - continuar rodando para próximas tentativas
        }

        // Loop de importação em background
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Aguardar intervalo configurado
                var delay = TimeSpan.FromMinutes(60); // Default 1 hora entre imports
                await Task.Delay(delay, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    break;

                _logger.LogInformation(
                    "Performing periodic import for {ListName}",
                    _config.ListName);

                var progress = new Progress<ImportProgress>(ReportProgress);
                var metrics = await _importer.ImportDiffAsync(_config, progress, cancellationToken);

                _logger.LogInformation(
                    "Periodic diff import completed for {ListName}: {Inserted} items inserted, {Errors} errors, Time: {Time}",
                    _config.ListName,
                    metrics.TotalInserted,
                    metrics.TotalErrors,
                    metrics.ElapsedTime);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Periodic import cancelled for {ListName}", _config.ListName);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic import failed for {ListName}", _config.ListName);
                // Continuar o loop mesmo se falhar
            }
        }

        _logger.LogInformation("ImportListBackgroundService stopped for {ListName}", _config.ListName);
    }

    private void ReportProgress(ImportProgress progress)
    {
        var metrics = progress.Metrics;
        
        if (metrics.TotalProcessed > 0 && metrics.TotalProcessed % 1000 == 0)
        {
            _logger.LogInformation(
                "[{ListName}] Progress: {Processed}/{Estimated} items, {ItemsPerSec:F2} items/s, {OpsPerSec:F2} ops/s, " +
                "Latency: {AvgLatency:F0}ms (p95: {P95}ms, p99: {P99}ms), Errors: {Errors} ({ErrorRate:F2}%), ETA: {ETA}",
                _config.ListName,
                metrics.TotalProcessed,
                metrics.EstimatedTotalItems > 0 ? metrics.EstimatedTotalItems : "?",
                metrics.ItemsPerSecond,
                metrics.OperationsPerSecond,
                metrics.AverageLatencyMs,
                metrics.P95LatencyMs,
                metrics.P99LatencyMs,
                metrics.TotalErrors,
                metrics.ErrorRatePercent,
                metrics.EstimatedTimeRemaining?.ToString("hh\\:mm\\:ss") ?? "?");
        }
    }
}


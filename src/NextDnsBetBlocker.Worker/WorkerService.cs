namespace NextDnsBetBlocker.Worker;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class WorkerService : BackgroundService
{
    private readonly IBetBlockerPipeline _pipeline;
    private readonly ILogger<WorkerService> _logger;
    private readonly WorkerSettings _settings;

    public WorkerService(
        IBetBlockerPipeline pipeline,
        ILogger<WorkerService> logger,
        WorkerSettings settings)
    {
        _pipeline = pipeline;
        _logger = logger;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker service starting");

        // Initial HaGeZi refresh
        try
        {
            await _pipeline.UpdateHageziAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh HaGeZi on startup");
        }

        // Run processing and HaGeZi update tasks concurrently
        var processingTask = ProcessLogsPeriodicAsync(stoppingToken);
        var hageziTask = UpdateHageziPeriodicAsync(stoppingToken);
        await Task.WhenAll(processingTask, hageziTask);
    }

    // Cada task com seu próprio timer
    private async Task ProcessLogsPeriodicAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.ProcessingIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _pipeline.ProcessLogsAsync(_settings.NextDnsProfileId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing logs");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("ProcessLogs task cancelled");
        }
    }

    private async Task UpdateHageziPeriodicAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(_settings.HageziRefreshIntervalHours));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _pipeline.UpdateHageziAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing HaGeZi");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("UpdateHaGeZi task cancelled");
        }
    }
}

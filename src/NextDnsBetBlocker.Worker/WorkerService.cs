namespace NextDnsBetBlocker.Worker;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

public class WorkerService : BackgroundService
{
    private readonly IBetBlockerPipeline _pipeline;
    private readonly ILogger<WorkerService> _logger;
    private readonly WorkerSettings _settings;
    private readonly IDistributedLockProvider _lockProvider;

    public WorkerService(
        IBetBlockerPipeline pipeline,
        ILogger<WorkerService> logger,
        IOptions<WorkerSettings> optionsSettings,
        IDistributedLockProvider lockProvider)
    {
        _pipeline = pipeline;
        _logger = logger;
        _settings = optionsSettings.Value;
        _lockProvider = lockProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker service starting");
               
        // Run processing and HaGeZi update tasks concurrently
        await ProcessLogsPeriodicAsync(stoppingToken);                
    }

    // Cada task com seu próprio timer
    private async Task ProcessLogsPeriodicAsync(CancellationToken stoppingToken)
    {

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.ProcessingIntervalSeconds));
        bool isFirst = true;

        try
        {            

            while (isFirst || await timer.WaitForNextTickAsync(stoppingToken))
            {
                var lockAcquired = await _lockProvider.TryAcquireLockAsync(
                _settings.LockKey,
                lockDurationSeconds: 60,
                cancellationToken: CancellationToken.None);

                if (!lockAcquired)
                {
                    _logger.LogInformation(
                        "Execution skipped - distributed lock is held by another instance. Next attempt in 1 minute.");
                    continue ;
                }

                try
                {
                    await _pipeline.ProcessLogsAsync(_settings.NextDnsProfileId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing logs");
                }
                finally
                {
                    isFirst = false;
                    await _lockProvider.ReleaseLockAsync(_settings.LockKey);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("ProcessLogs task cancelled");
        }
    }
}

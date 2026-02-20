using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

namespace NextDnsBetBlocker.FunctionApp;

/// <summary>
/// AnalysisFunction
/// 
/// Azure Function triggered on a schedule to:
/// - Fetch NextDNS logs
/// - Classify domains (using Tranco cache)
/// - Publish suspicious domains to Azure Queue for analysis/blocking
/// 
/// Mirrors the WorkerService from the Worker project.
/// Uses distributed locking to prevent concurrent execution across multiple instances.
/// </summary>
public class AnalysisFunction
{
    private readonly IBetBlockerPipeline _pipeline;
    private readonly ILogger<AnalysisFunction> _logger;
    private readonly WorkerSettings _settings;
    private readonly IDistributedLockProvider _lockProvider;

    private const string LockName = "analysis-function-lock";

    public AnalysisFunction(
        IBetBlockerPipeline pipeline,
        ILogger<AnalysisFunction> logger,
        IOptions<WorkerSettings> optionsSettings,
        IDistributedLockProvider lockProvider)
    {
        _pipeline = pipeline;
        _logger = logger;
        _settings = optionsSettings.Value;
        _lockProvider = lockProvider;
    }

    /// <summary>
    /// Processes NextDNS logs on a schedule defined by FunctionSchedules:AnalysisSchedule in appsettings.json.
    /// The schedule can be overridden by setting the CRON_EXPRESSION environment variable in Azure.
    /// Default: Every minute (0 */1 * * * *)
    /// 
    /// Uses distributed locking to ensure only one instance processes logs at a time.
    /// If another instance is already processing, this execution is skipped.
    /// </summary>
    [Function("AnalysisFunction")]
    public async Task RunAsync([TimerTrigger("%FunctionSchedules:AnalysisSchedule%")] TimerInfo timerInfo)
    {
        try
        {
            _logger.LogInformation("AnalysisFunction triggered at {time}", DateTime.UtcNow);

            // Try to acquire distributed lock (45 seconds to account for execution time)
            var lockAcquired = await _lockProvider.TryAcquireLockAsync(
                LockName,
                lockDurationSeconds: 45,
                cancellationToken: CancellationToken.None);

            if (!lockAcquired)
            {
                _logger.LogInformation(
                    "Execution skipped - distributed lock is held by another instance. Next attempt in 1 minute.");
                return;
            }

            try
            {
                // Perform the actual work
                await _pipeline.ProcessLogsAsync(_settings.NextDnsProfileId);

                if (timerInfo?.ScheduleStatus != null)
                {
                    _logger.LogInformation(
                        "Logs processed successfully. Next execution scheduled for {nextSchedule}",
                        timerInfo.ScheduleStatus.Next);
                }
            }
            finally
            {
                // Release the lock
                await _lockProvider.ReleaseLockAsync(LockName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing logs in AnalysisFunction");
            throw;
        }
    }
}
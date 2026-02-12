using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

namespace NextDnsBetBlocker.FunctionApp;

public class ProcessLogsFunction
{
    private readonly IBetBlockerPipeline _pipeline;
    private readonly FunctionAppSettings _settings;

    public ProcessLogsFunction(IBetBlockerPipeline pipeline, FunctionAppSettings settings)
    {
        _pipeline = pipeline;
        _settings = settings;
    }

    [FunctionName("ProcessLogs")]
    public async Task Run(
        [TimerTrigger("0 */30 * * * *")] TimerInfo myTimer,
        ILogger log)
    {
        log.LogInformation("ProcessLogs function started at {Time}", DateTime.Now);

        try
        {
            var stats = await _pipeline.ProcessLogsAsync(
                _settings.NextDnsProfileId,
                _settings.NextDnsApiKey);

            log.LogInformation(
                "ProcessLogs completed. Blocked: {Blocked}, Skipped: {Skipped}, Duration: {Duration}",
                stats.DomainsBlocked, stats.DomainsSkipped, stats.Duration);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "ProcessLogs function failed");
            throw;
        }
    }
}

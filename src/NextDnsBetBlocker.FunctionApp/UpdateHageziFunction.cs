using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

namespace NextDnsBetBlocker.FunctionApp;

public class UpdateHageziFunction
{
    private readonly IBetBlockerPipeline _pipeline;

    public UpdateHageziFunction(IBetBlockerPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    [FunctionName("UpdateHagezi")]
    public async Task Run(
        [TimerTrigger("0 0 0 * * *")] TimerInfo myTimer,
        ILogger log)
    {
        log.LogInformation("UpdateHagezi function started at {Time}", DateTime.Now);

        try
        {
            await _pipeline.UpdateHageziAsync();
            log.LogInformation("UpdateHagezi completed successfully");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "UpdateHagezi function failed");
            throw;
        }
    }
}

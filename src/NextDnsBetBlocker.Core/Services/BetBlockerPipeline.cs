namespace NextDnsBetBlocker.Core.Services;

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

public class BetBlockerPipeline : IBetBlockerPipeline
{    
    private readonly IHageziProvider _hageziProvider;
    private readonly ILogger<BetBlockerPipeline> _logger;
    private readonly ILogsProducer _logsProducer;
    private readonly IClassifierConsumer _classifierConsumer;
    private readonly ITrancoAllowlistConsumer _trancoAllowlistConsumer;
    private readonly IAnalysisConsumer _analysisConsumer;

    public BetBlockerPipeline(        
        IHageziProvider hageziProvider,
        ILogger<BetBlockerPipeline> logger,
        ILogsProducer logsProducer,
        IClassifierConsumer classifierConsumer,
        ITrancoAllowlistConsumer trancoAllowlistConsumer,
        IAnalysisConsumer analysisConsumer)
    {        
        _logger = logger;
        _logsProducer = logsProducer;
        _classifierConsumer = classifierConsumer;
        _trancoAllowlistConsumer = trancoAllowlistConsumer;
        _analysisConsumer = analysisConsumer;
        _hageziProvider = hageziProvider;
    }

    public async Task<BlockerRunStatistics> ProcessLogsAsync(string profileId)
    {
        var stats = new BlockerRunStatistics
        {
            StartTime = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Starting pipeline with Channels for profile {ProfileId}", profileId);

            // Create channels for pipeline
            var logsChannel = Channel.CreateBounded<LogEntryData>(new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var suspectsChannel = Channel.CreateBounded<SuspectDomainEntry>(new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var trancoFilteredChannel = Channel.CreateBounded<SuspectDomainEntry>(new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            using var cts = new CancellationTokenSource();

            try
            {
                // Run all pipeline stages in parallel
                // Order: LogsProducer -> TrancoAllowlistConsumer -> ClassifierConsumer -> AnalysisConsumer
                var tasks = new[]
                {
                    _logsProducer.StartAsync(logsChannel, profileId, cts.Token),
                    _trancoAllowlistConsumer.StartAsync(logsChannel, suspectsChannel, profileId, cts.Token),
                    _classifierConsumer.StartAsync(suspectsChannel, trancoFilteredChannel, profileId, cts.Token),
                    _analysisConsumer.StartAsync(trancoFilteredChannel, profileId, cts.Token)
                };

                _logger.LogInformation("Pipeline stages started");
                await Task.WhenAll(tasks);
                _logger.LogInformation("Pipeline completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline error, requesting cancellation");
                cts.Cancel();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed with error");
            throw;
        }
        finally
        {
            stats.EndTime = DateTime.UtcNow;
            LogStatistics(stats);
        }

        return stats;
    }

    [Obsolete("This method is deprecated. The HaGeZi list is now updated automatically on a daily schedule. Manual refresh is no longer necessary.", true)]
    public async Task UpdateHageziAsync()
    {
        try
        {
            _logger.LogInformation("Starting HaGeZi update");
            await _hageziProvider.RefreshAsync();
            _logger.LogInformation("HaGeZi update completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HaGeZi update failed");
            throw;
        }
    }

    private void LogStatistics(BlockerRunStatistics stats)
    {
        _logger.LogInformation(
            "Pipeline Statistics: Duration={Duration:mm\\:ss}, " +
            "DomainsLogged={DomainsLogged}, UniqueDomains={UniqueDomains}, " +
            "DomainsBlocked={DomainsBlocked}, DomainsSkipped={DomainsSkipped}, " +
            "DomainsAllowlisted={DomainsAllowlisted}, DomainsAlreadyBlocked={DomainsAlreadyBlocked}, " +
            "HageziDomains={HageziDomains}",
            stats.Duration, stats.DomainsLogged, stats.UniqueDomains,
            stats.DomainsBlocked, stats.DomainsSkipped, stats.DomainsAllowlisted,
            stats.DomainsAlreadyBlocked, stats.HageziTotalDomains);
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().ToLowerInvariant().TrimEnd('.');
    }
}

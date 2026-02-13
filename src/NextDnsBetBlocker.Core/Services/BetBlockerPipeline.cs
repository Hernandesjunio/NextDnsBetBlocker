namespace NextDnsBetBlocker.Core.Services;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

public class BetBlockerPipeline : IBetBlockerPipeline
{
    private readonly INextDnsClient _nextDnsClient;
    private readonly ICheckpointStore _checkpointStore;
    private readonly IBlockedDomainStore _blockedDomainStore;
    private readonly IHageziProvider _hageziProvider;
    private readonly IAllowlistProvider _allowlistProvider;
    private readonly IBetClassifier _betClassifier;
    private readonly ILogger<BetBlockerPipeline> _logger;
    private readonly int _rateLimitPerSecond;

    public BetBlockerPipeline(
        INextDnsClient nextDnsClient,
        ICheckpointStore checkpointStore,
        IBlockedDomainStore blockedDomainStore,
        IHageziProvider hageziProvider,
        IAllowlistProvider allowlistProvider,
        IBetClassifier betClassifier,
        ILogger<BetBlockerPipeline> logger,
        int rateLimitPerSecond = 5)
    {
        _nextDnsClient = nextDnsClient;
        _checkpointStore = checkpointStore;
        _blockedDomainStore = blockedDomainStore;
        _hageziProvider = hageziProvider;
        _allowlistProvider = allowlistProvider;
        _betClassifier = betClassifier;
        _logger = logger;
        _rateLimitPerSecond = rateLimitPerSecond;
    }

    public async Task<BlockerRunStatistics> ProcessLogsAsync(string profileId)
    {
        var stats = new BlockerRunStatistics
        {
            StartTime = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Starting bet blocker pipeline for profile {ProfileId}", profileId);

            // Reload allowlist
            await _allowlistProvider.ReloadAsync();
            var allowlist = _allowlistProvider.GetAllowlist();
            _logger.LogInformation("Loaded allowlist with {Count} domains", allowlist.Count);

            // Get last checkpoint
            var lastTimestamp = await _checkpointStore.GetLastTimestampAsync(profileId);
            _logger.LogInformation("Last checkpoint: {Timestamp}", lastTimestamp?.ToString("O") ?? "None");

            // Fetch logs with pagination
            var allDomains = new List<string>();
            string? cursor = null;
            var newLastTimestamp = lastTimestamp ?? DateTime.MinValue;

            do
            {
                _logger.LogInformation("Fetching logs for profile {ProfileId}, cursor: {Cursor}, since: {Since}", 
                    profileId, cursor ?? "initial", lastTimestamp?.ToString("O") ?? "beginning");

                var response = await _nextDnsClient.GetLogsAsync(profileId, cursor, since: lastTimestamp);

                if (response.Data.Count == 0)
                {
                    _logger.LogInformation("No logs returned");
                    break;
                }

                // Filter logs from checkpoint onwards and collect domains
                foreach (var log in response.Data)
                {
                    // Use >= instead of > to capture logs at the exact checkpoint timestamp
                    if (log.Timestamp >= (lastTimestamp ?? DateTime.MinValue))
                    {
                        allDomains.Add(log.Domain);
                        newLastTimestamp = log.Timestamp > newLastTimestamp ? log.Timestamp : newLastTimestamp;
                        _logger.LogDebug("Collecting domain {Domain} from timestamp {Timestamp}", log.Domain, log.Timestamp);
                    }
                }

                stats.DomainsLogged += response.Data.Count;
                cursor = response.Meta.Pagination.Cursor;

            } while (!string.IsNullOrEmpty(cursor));

            _logger.LogInformation("Total logs fetched: {Count}", stats.DomainsLogged);

            // Normalize and deduplicate domains
            var uniqueDomains = allDomains
                .Select(NormalizeDomain)
                .Where(d => !string.IsNullOrEmpty(d))
                .ToHashSet();

            stats.UniqueDomains = uniqueDomains.Count;
            _logger.LogInformation("Unique domains after deduplication: {Count}", stats.UniqueDomains);

            // Get HaGeZi gambling domains
            var gamblingDomains = await _hageziProvider.GetGamblingDomainsAsync();
            stats.HageziTotalDomains = gamblingDomains.Count;
            _logger.LogInformation("HaGeZi gambling list contains {Count} domains", stats.HageziTotalDomains);

            // Process each domain
            var delayBetweenRequests = TimeSpan.FromMilliseconds(1000.0 / _rateLimitPerSecond);

            foreach (var domain in uniqueDomains)
            {
                // Check if allowlisted
                if (allowlist.Contains(domain))
                {
                    stats.DomainsAllowlisted++;
                    _logger.LogDebug("Domain {Domain} is allowlisted, skipping", domain);
                    continue;
                }

                // Check if already blocked
                if (await _blockedDomainStore.IsBlockedAsync(profileId, domain))
                {
                    stats.DomainsAlreadyBlocked++;
                    _logger.LogDebug("Domain {Domain} already blocked, skipping", domain);
                    continue;
                }

                // Check if it's a bet domain
                if (!_betClassifier.IsBetDomain(domain))
                {
                    stats.DomainsSkipped++;
                    _logger.LogDebug("Domain {Domain} is not classified as bet/gambling", domain);
                    continue;
                }

                // Add to denylist
                var request = new DenylistBlockRequest { Id = domain, Active = true };
                var success = await _nextDnsClient.AddToDenylistAsync(profileId, request);

                if (success)
                {
                    await _blockedDomainStore.MarkBlockedAsync(profileId, domain);
                    stats.DomainsBlocked++;
                    _logger.LogInformation("Successfully blocked domain {Domain}", domain);
                }

                // Rate limiting
                await Task.Delay(delayBetweenRequests);
            }

            // Update checkpoint
            if (newLastTimestamp > (lastTimestamp ?? DateTime.MinValue))
            {
                _logger.LogInformation("Updating checkpoint: Old={OldTimestamp}, New={NewTimestamp}", 
                    (lastTimestamp ?? DateTime.MinValue).ToString("O"), newLastTimestamp.ToString("O"));
                await _checkpointStore.UpdateLastTimestampAsync(profileId, newLastTimestamp);
                _logger.LogInformation("✓ Checkpoint updated successfully to {Timestamp}", newLastTimestamp.ToString("O"));
            }
            else
            {
                _logger.LogWarning("⚠ Checkpoint NOT updated - newLastTimestamp ({New}) is NOT greater than lastTimestamp ({Last})", 
                    newLastTimestamp.ToString("O"), (lastTimestamp ?? DateTime.MinValue).ToString("O"));
            }

            _logger.LogInformation("Pipeline completed successfully");
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

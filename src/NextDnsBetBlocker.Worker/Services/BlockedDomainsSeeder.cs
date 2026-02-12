namespace NextDnsBetBlocker.Worker.Services;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class BlockedDomainsSeeder
{
    private readonly IBlockedDomainStore _blockedDomainStore;
    private readonly ICheckpointStore _checkpointStore;
    private readonly ILogger<BlockedDomainsSeeder> _logger;
    private const string SeedCheckpointKey = "SEED_BLOCKED_DOMAINS";

    public BlockedDomainsSeeder(
        IBlockedDomainStore blockedDomainStore,
        ICheckpointStore checkpointStore,
        ILogger<BlockedDomainsSeeder> logger)
    {
        _blockedDomainStore = blockedDomainStore;
        _checkpointStore = checkpointStore;
        _logger = logger;
    }

    public async Task SeedBlockedDomainsAsync(string profileId, string blockedDomainsFilePath)
    {
        try
        {
            // Check if seed has already been done
            var seedCheckpoint = await _checkpointStore.GetLastTimestampAsync(SeedCheckpointKey);
            if (seedCheckpoint.HasValue)
            {
                _logger.LogInformation("Blocked domains seed has already been completed at {Timestamp}", seedCheckpoint);
                return;
            }

            // File doesn't exist, skip seed
            if (!File.Exists(blockedDomainsFilePath))
            {
                _logger.LogWarning("Blocked domains file not found at {FilePath}", blockedDomainsFilePath);
                return;
            }

            _logger.LogInformation("Starting seed of blocked domains from {FilePath}", blockedDomainsFilePath);

            // Read and parse domains
            var domains = ParseBlockedDomains(blockedDomainsFilePath);
            _logger.LogInformation("Parsed {Count} domains from blocked domains file", domains.Count);

            // Mark all domains as blocked
            int successCount = 0;
            int skipCount = 0;

            foreach (var domain in domains)
            {
                try
                {
                    var isAlreadyBlocked = await _blockedDomainStore.IsBlockedAsync(profileId, domain);
                    if (!isAlreadyBlocked)
                    {
                        await _blockedDomainStore.MarkBlockedAsync(profileId, domain);
                        successCount++;
                    }
                    else
                    {
                        skipCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to mark domain {Domain} as blocked", domain);
                }
            }

            // Mark seed as completed
            await _checkpointStore.UpdateLastTimestampAsync(SeedCheckpointKey, DateTime.UtcNow);

            _logger.LogInformation(
                "Blocked domains seed completed: {SuccessCount} domains added, {SkipCount} already blocked",
                successCount,
                skipCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed blocked domains");
            throw;
        }
    }

    private static List<string> ParseBlockedDomains(string filePath)
    {
        var domains = new List<string>();

        using var reader = new StreamReader(filePath);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            {
                continue;
            }

            // Remove leading/trailing whitespace
            line = line.Trim();

            // Remove wildcard prefix if present
            if (line.StartsWith("*."))
            {
                line = line[2..];
            }

            // Skip if still empty
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            domains.Add(line);
        }

        // Remove duplicates
        return domains.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

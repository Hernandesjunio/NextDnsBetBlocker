namespace NextDnsBetBlocker.Core.Services;

using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

public class GamblingSuspectStore : IGamblingSuspectStore
{
    private readonly TableClient _tableClient;
    private readonly ILogger<GamblingSuspectStore> _logger;
    private const string PartitionKeyPending = "pending";
    private const string PartitionKeyAnalyzed = "analyzed";
    private const string PartitionKeyWhitelist = "whitelist";

    public GamblingSuspectStore(TableClient tableClient, ILogger<GamblingSuspectStore> logger)
    {
        _tableClient = tableClient;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the table on first access (idempotent)
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("GamblingSuspects table initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize GamblingSuspects table");
            throw;
        }
    }

    public async Task EnqueueForAnalysisAsync(string domain)
    {
        try
        {
            var normalizedDomain = domain.ToLowerInvariant().Trim();

            // Check if already analyzed
            try
            {
                var existing = await _tableClient.GetEntityAsync<TableEntity>(PartitionKeyAnalyzed, normalizedDomain);
                if (existing.Value != null)
                {
                    _logger.LogDebug("Domain {Domain} already analyzed, skipping queue", normalizedDomain);
                    return;
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Not found, proceed
            }

            var entity = new TableEntity(PartitionKeyPending, normalizedDomain)
            {
                { "EnqueuedAt", DateTime.UtcNow },
                { "AccessCount", 1 },
                { "Status", AnalysisStatus.Pending.ToString() }
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            _logger.LogInformation("Domain {Domain} enqueued for analysis", normalizedDomain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue domain {Domain} for analysis", domain);
            throw;
        }
    }

    public async Task<IEnumerable<string>> GetPendingDomainsAsync(int limit = 100)
    {
        try
        {
            var query = _tableClient.QueryAsync<TableEntity>(
                x => x.PartitionKey == PartitionKeyPending,
                maxPerPage: limit);

            var domains = new List<string>();
            await foreach (var entity in query)
            {
                domains.Add(entity.RowKey);
                if (domains.Count >= limit)
                    break;
            }

            _logger.LogDebug("Retrieved {Count} pending domains for analysis", domains.Count);
            return domains;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve pending domains");
            return Enumerable.Empty<string>();
        }
    }

    public async Task SaveAnalysisResultAsync(GamblingSuspect suspect)
    {
        try
        {
            var normalizedDomain = suspect.Domain.ToLowerInvariant().Trim();

            // Save analyzed result
            var entity = new TableEntity(PartitionKeyAnalyzed, normalizedDomain)
            {
                { "FirstSeen", suspect.FirstSeen },
                { "AccessCount", suspect.AccessCount },
                { "Status", suspect.Status.ToString() },
                { "ConfidenceScore", suspect.ConfidenceScore },
                { "GamblingIndicators", string.Join(";", suspect.GamblingIndicators) },
                { "DomainAgeInDays", suspect.DomainAgeInDays },
                { "LastAnalyzed", suspect.LastAnalyzed ?? DateTime.UtcNow },
                { "BlockReason", suspect.BlockReason },
                { "IsWhitelisted", suspect.IsWhitelisted },
                { "SslIssuer", suspect.SslIssuer },
                { "SslExpiryDate", suspect.SslExpiryDate },
                { "SuspiciousDnsRecords", suspect.SuspiciousDnsRecords },
                { "AnalysisDetails", suspect.AnalysisDetails }
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);

            // Remove from pending if exists
            try
            {
                await _tableClient.DeleteEntityAsync(PartitionKeyPending, normalizedDomain);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Not in pending, that's okay
            }

            if (suspect.IsWhitelisted)
            {
                // Add to whitelist
                var whitelistEntity = new TableEntity(PartitionKeyWhitelist, normalizedDomain)
                {
                    { "WhitelistedAt", DateTime.UtcNow },
                    { "Reason", suspect.BlockReason }
                };
                await _tableClient.UpsertEntityAsync(whitelistEntity, TableUpdateMode.Replace);
            }

            _logger.LogInformation(
                "Analysis saved for {Domain}: Status={Status}, Score={Score}, IsWhitelisted={IsWhitelisted}",
                normalizedDomain, suspect.Status, suspect.ConfidenceScore, suspect.IsWhitelisted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save analysis result for domain {Domain}", suspect.Domain);
            throw;
        }
    }

    public async Task<bool> IsWhitelistedAsync(string domain)
    {
        try
        {
            var normalizedDomain = domain.ToLowerInvariant().Trim();
            var response = await _tableClient.GetEntityAsync<TableEntity>(PartitionKeyWhitelist, normalizedDomain);
            return response.Value != null;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking whitelist for domain {Domain}", domain);
            return false;
        }
    }

    public async Task<GamblingSuspect?> GetSuspectAsync(string domain)
    {
        try
        {
            var normalizedDomain = domain.ToLowerInvariant().Trim();
            var response = await _tableClient.GetEntityAsync<TableEntity>(PartitionKeyAnalyzed, normalizedDomain);

            if (response.Value == null)
                return null;

            var entity = response.Value;
            return new GamblingSuspect
            {
                Domain = normalizedDomain,
                FirstSeen = entity.GetDateTimeOffset("FirstSeen")?.UtcDateTime ?? DateTime.UtcNow,
                AccessCount = (int?)entity["AccessCount"] ?? 0,
                Status = Enum.Parse<AnalysisStatus>(entity["Status"]?.ToString() ?? AnalysisStatus.Pending.ToString()),
                ConfidenceScore = (int?)entity["ConfidenceScore"] ?? 0,
                GamblingIndicators = entity["GamblingIndicators"]?.ToString()?.Split(';').ToList() ?? [],
                DomainAgeInDays = (int?)entity["DomainAgeInDays"] ?? 0,
                LastAnalyzed = entity.GetDateTimeOffset("LastAnalyzed")?.UtcDateTime,
                BlockReason = entity["BlockReason"]?.ToString() ?? string.Empty,
                IsWhitelisted = (bool?)entity["IsWhitelisted"] ?? false,
                SslIssuer = entity["SslIssuer"]?.ToString() ?? string.Empty,
                SslExpiryDate = entity.GetDateTimeOffset("SslExpiryDate")?.UtcDateTime,
                SuspiciousDnsRecords = (int?)entity["SuspiciousDnsRecords"] ?? 0,
                AnalysisDetails = entity["AnalysisDetails"]?.ToString() ?? string.Empty
            };
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving suspect for domain {Domain}", domain);
            return null;
        }
    }
}

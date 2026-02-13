namespace NextDnsBetBlocker.Core.Services;

using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class HageziProvider : IHageziProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<HageziProvider> _logger;
    private readonly string _localCachePath;
    private readonly IHttpClientFactory _httpClientFactory;
    private HashSet<string> _gamblingDomains = [];
    private DateTime _lastUpdate = DateTime.MinValue;

    // Blob storage paths
    private const string BlobNameAdblock = "hagezi-gambling-adblock.txt";
    private const string BlobNameWildcard = "hagezi-gambling-wildcard.txt";

    // HaGeZi URLs - Full versions (189.650 domains base + wildcards for subdomains)
    private const string HageziUrlAdblock = "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/adblock/gambling.txt";
    private const string HageziUrlWildcard = "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/gambling.txt";

    public HageziProvider(
        BlobContainerClient containerClient,
        string localCachePath,
        IHttpClientFactory httpClientFactory,
        ILogger<HageziProvider> logger)
    {
        _containerClient = containerClient;
        _localCachePath = localCachePath;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HashSet<string>> GetGamblingDomainsAsync()
    {
        // If cache is fresh (less than 1 hour old), return it
        if (_gamblingDomains.Count > 0 && DateTime.UtcNow - _lastUpdate < TimeSpan.FromHours(1))
        {
            return _gamblingDomains;
        }

        // Try to load from local cache or blob storage
        if (_gamblingDomains.Count == 0)
        {
            await LoadFromCacheAsync();
        }

        return _gamblingDomains;
    }

    public async Task RefreshAsync()
    {
        try
        {
            _logger.LogInformation("Refreshing HaGeZi gambling list from multiple sources");

            var httpClient = _httpClientFactory.CreateClient();

            // Download both formats for maximum coverage
            _logger.LogInformation("Downloading adblock format from {Url}", HageziUrlAdblock);
            var adblockContent = await httpClient.GetStringAsync(HageziUrlAdblock);

            _logger.LogInformation("Downloading wildcard format from {Url}", HageziUrlWildcard);
            var wildcardContent = await httpClient.GetStringAsync(HageziUrlWildcard);

            // Parse both formats and merge
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var adblockDomains = ParseAdblockFormat(adblockContent);
            var wildcardDomains = ParseWildcardFormat(wildcardContent);

            foreach (var domain in adblockDomains)
                domains.Add(domain);
            foreach (var domain in wildcardDomains)
                domains.Add(domain);

            _logger.LogInformation("Parsed {AdblockCount} domains from adblock format", adblockDomains.Count);
            _logger.LogInformation("Parsed {WildcardCount} domains from wildcard format", wildcardDomains.Count);
            _logger.LogInformation("Total unique gambling domains after merge: {TotalCount}", domains.Count);

            // Save to local cache
            await File.WriteAllTextAsync(_localCachePath, string.Join(Environment.NewLine, domains));
            _logger.LogInformation("Saved HaGeZi gambling list to local cache: {Path}", _localCachePath);

            // Save to blob storage
            try
            {
                var blobClientAdblock = _containerClient.GetBlobClient(BlobNameAdblock);
                var blobClientWildcard = _containerClient.GetBlobClient(BlobNameWildcard);

                using var streamAdblock = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(adblockContent));
                await blobClientAdblock.UploadAsync(streamAdblock, overwrite: true);

                using var streamWildcard = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(wildcardContent));
                await blobClientWildcard.UploadAsync(streamWildcard, overwrite: true);

                _logger.LogInformation("Uploaded HaGeZi gambling lists to blob storage");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload HaGeZi gambling lists to blob storage, continuing with local cache");
            }

            _gamblingDomains = domains;
            _lastUpdate = DateTime.UtcNow;
            _logger.LogInformation("HaGeZi gambling list refreshed successfully: {Count} total domains", domains.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh HaGeZi gambling list");
            throw;
        }
    }

    private async Task LoadFromCacheAsync()
    {
        // Try local cache first
        if (File.Exists(_localCachePath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(_localCachePath);
                _gamblingDomains = new HashSet<string>(content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                _lastUpdate = DateTime.UtcNow;
                _logger.LogInformation("Loaded HaGeZi gambling list from local cache: {Count} domains", _gamblingDomains.Count);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load from local cache");
            }
        }

        // Try blob storage (both adblock and wildcard formats)
        try
        {
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Load adblock format
            try
            {
                var blobClientAdblock = _containerClient.GetBlobClient(BlobNameAdblock);
                var downloadAdblock = await blobClientAdblock.DownloadAsync();
                using var srAdblock = new StreamReader(downloadAdblock.Value.Content);
                var contentAdblock = await srAdblock.ReadToEndAsync();
                var adblockDomains = ParseAdblockFormat(contentAdblock);
                foreach (var domain in adblockDomains)
                    domains.Add(domain);
                _logger.LogInformation("Loaded {Count} domains from blob storage (adblock format)", adblockDomains.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load adblock format from blob storage");
            }

            // Load wildcard format
            try
            {
                var blobClientWildcard = _containerClient.GetBlobClient(BlobNameWildcard);
                var downloadWildcard = await blobClientWildcard.DownloadAsync();
                using var srWildcard = new StreamReader(downloadWildcard.Value.Content);
                var contentWildcard = await srWildcard.ReadToEndAsync();
                var wildcardDomains = ParseWildcardFormat(contentWildcard);
                foreach (var domain in wildcardDomains)
                    domains.Add(domain);
                _logger.LogInformation("Loaded {Count} domains from blob storage (wildcard format)", wildcardDomains.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load wildcard format from blob storage");
            }

            if (domains.Count > 0)
            {
                _gamblingDomains = domains;
                _lastUpdate = DateTime.UtcNow;
                _logger.LogInformation("Loaded HaGeZi gambling list from blob storage: {Count} total domains", domains.Count);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load from blob storage");
        }

        _logger.LogWarning("Could not load HaGeZi list from any source, using empty set");
        _gamblingDomains = [];
    }

    private static HashSet<string> ParseAdblockFormat(string content)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.AsSpan().Trim();

            // Skip comments and empty lines
            if (trimmed.IsEmpty || trimmed[0] == '[' || trimmed[0] == '!')
                continue;

            // Extract domain from adblock format (e.g., "||example.com^" becomes "example.com")
            var domain = trimmed;

            // Remove || prefix
            if (domain.StartsWith("||"))
                domain = domain[2..];

            // Remove ^ suffix
            if (domain.EndsWith("^"))
                domain = domain[..^1];

            domain = domain.Trim();

            if (!domain.IsEmpty && domain.Contains('.'))
            {
                domains.Add(domain.ToString().ToLowerInvariant());
            }
        }

        return domains;
    }

    private static HashSet<string> ParseWildcardFormat(string content)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.AsSpan().Trim();

            // Skip comments and empty lines
            if (trimmed.IsEmpty || trimmed[0] == '[' || trimmed[0] == '!')
                continue;

            // Extract domain from wildcard format (e.g., "*.example.com" becomes "example.com")
            var domain = trimmed;

            // Remove *. prefix
            if (domain.StartsWith("*."))
                domain = domain[2..];

            // Remove || prefix (in case format varies)
            if (domain.StartsWith("||"))
                domain = domain[2..];

            // Remove ^ suffix
            if (domain.EndsWith("^"))
                domain = domain[..^1];

            domain = domain.Trim();

            if (!domain.IsEmpty && domain.Contains('.'))
            {
                domains.Add(domain.ToString().ToLowerInvariant());
            }
        }

        return domains;
    }
}

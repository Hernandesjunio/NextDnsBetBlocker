namespace NextDnsBetBlocker.Core.Services;

using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// HaGeZi Gambling Domains Provider
/// URLs configuráveis via IOptions<HageziProviderConfig>
/// Cache de 1 hora para melhor performance
/// </summary>
public class HageziProvider : IHageziProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<HageziProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _adblockUrl;
    private readonly string _wildcardUrl;
    private readonly int _cacheExpireHours;
    private HashSet<string> _gamblingDomains = [];
    private DateTime _lastUpdate = DateTime.MinValue;

    // Blob storage paths
    private const string BlobNameAdblock = "hagezi-gambling-adblock.txt";
    private const string BlobNameWildcard = "hagezi-gambling-wildcard.txt";
    private const string ContainerName = "hagezi-lists";

    /// <summary>
    /// <summary>
    /// Constructor with hardcoded container name ("hagezi-lists")
    /// Used for both Analysis layer and Importer layer
    /// </summary>
    public HageziProvider(
        BlobServiceClient blobServiceClient,
        IHttpClientFactory httpClientFactory,
        ILogger<HageziProvider> logger,
        IOptions<HageziProviderConfig> options)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        _containerClient = containerClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var config = options.Value;
        _adblockUrl = config.AdblockUrl;
        _wildcardUrl = config.WildcardUrl;
        _cacheExpireHours = config.CacheExpireHours;

        if (string.IsNullOrEmpty(_adblockUrl) || string.IsNullOrEmpty(_wildcardUrl))
        {
            throw new InvalidOperationException(
                "HaGeZi:AdblockUrl and HaGeZi:WildcardUrl must be configured in appsettings or User Secrets");
        }

        _logger.LogInformation(
            "HageziProvider initialized with ContainerName: {ContainerName}, AdblockUrl: {AdblockUrl}, WildcardUrl: {WildcardUrl}, CacheExpire: {Hours}h",
            ContainerName, _adblockUrl, _wildcardUrl, _cacheExpireHours);
    }

    public async Task<HashSet<string>> GetGamblingDomainsAsync()
    {
        // If cache is fresh, return it
        if (_gamblingDomains.Count > 0 && DateTime.UtcNow - _lastUpdate < TimeSpan.FromHours(_cacheExpireHours))
        {
            _logger.LogDebug("Returning cached HaGeZi gambling domains: {Count} domains", _gamblingDomains.Count);
            return _gamblingDomains;
        }

        // Try to load from blob storage
        if (_gamblingDomains.Count == 0)
        {
            await LoadFromBlobAsync();
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
            _logger.LogInformation("Downloading adblock format from {Url}", _adblockUrl);
            var adblockContent = await httpClient.GetStringAsync(_adblockUrl);

            _logger.LogInformation("Downloading wildcard format from {Url}", _wildcardUrl);
            var wildcardContent = await httpClient.GetStringAsync(_wildcardUrl);

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
                _logger.LogWarning(ex, "Failed to upload HaGeZi gambling lists to blob storage");
                throw;
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

    private async Task LoadFromBlobAsync()
    {
        try
        {
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Load adblock format
            try
            {
                var blobClientAdblock = _containerClient.GetBlobClient(BlobNameAdblock);
                if (await blobClientAdblock.ExistsAsync())
                {
                    var download = await blobClientAdblock.DownloadAsync();
                    using var sr = new StreamReader(download.Value.Content);
                    var content = await sr.ReadToEndAsync();
                    var adblockDomains = ParseAdblockFormat(content);
                    foreach (var domain in adblockDomains)
                        domains.Add(domain);
                    _logger.LogInformation("Loaded {Count} domains from adblock blob", adblockDomains.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load adblock format from blob");
            }

            // Load wildcard format
            try
            {
                var blobClientWildcard = _containerClient.GetBlobClient(BlobNameWildcard);
                if (await blobClientWildcard.ExistsAsync())
                {
                    var download = await blobClientWildcard.DownloadAsync();
                    using var sr = new StreamReader(download.Value.Content);
                    var content = await sr.ReadToEndAsync();
                    var wildcardDomains = ParseWildcardFormat(content);
                    foreach (var domain in wildcardDomains)
                        domains.Add(domain);
                    _logger.LogInformation("Loaded {Count} domains from wildcard blob", wildcardDomains.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load wildcard format from blob");
            }

            if (domains.Count > 0)
            {
                _gamblingDomains = domains;
                _lastUpdate = DateTime.UtcNow;
                _logger.LogInformation("Loaded HaGeZi gambling domains from blob storage: {Count} total domains", domains.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load HaGeZi gambling domains from blob storage");
        }
    }

    private HashSet<string> ParseAdblockFormat(string content)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();

            // Skip comments and empty lines
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("!"))
                continue;

            // Adblock format: || example.com^
            if (trimmed.StartsWith("||") && trimmed.EndsWith("^"))
            {
                var domain = trimmed[2..^1].Trim();
                if (!string.IsNullOrWhiteSpace(domain))
                    domains.Add(domain);
            }
        }

        return domains;
    }

    private HashSet<string> ParseWildcardFormat(string content)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();

            // Skip comments and empty lines
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                continue;

            // Remove wildcard prefix if present
            if (trimmed.StartsWith("*."))
                trimmed = trimmed[2..];

            if (!string.IsNullOrWhiteSpace(trimmed))
                domains.Add(trimmed);
        }

        return domains;
    }
}

/// <summary>
/// Configuração para HageziProvider
/// URLs lidas de appsettings.json ou User Secrets
/// </summary>
public class HageziProviderConfig
{
    public string AdblockUrl { get; set; } = "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/adblock/gambling.txt";
    public string WildcardUrl { get; set; } = "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/gambling.txt";
    public int CacheExpireHours { get; set; } = 1;
}

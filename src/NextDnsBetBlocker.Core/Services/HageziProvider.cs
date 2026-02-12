namespace NextDnsBetBlocker.Core.Services;

using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class HageziProvider : IHageziProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<HageziProvider> _logger;
    private readonly string _localCachePath;
    private HashSet<string> _gamblingDomains = [];
    private DateTime _lastUpdate = DateTime.MinValue;
    private const string BlobName = "hagezi-gambling-domains.txt";
    private const string HageziUrl = "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/gambling.txt";

    public HageziProvider(BlobContainerClient containerClient, string localCachePath, ILogger<HageziProvider> logger)
    {
        _containerClient = containerClient;
        _localCachePath = localCachePath;
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
            _logger.LogInformation("Refreshing HaGeZi gambling list from {Url}", HageziUrl);

            using var httpClient = new HttpClient();
            var content = await httpClient.GetStringAsync(HageziUrl);

            var domains = ParseAdblockFormat(content);
            
            // Save to local cache
            await File.WriteAllTextAsync(_localCachePath, content);
            _logger.LogInformation("Saved HaGeZi list to local cache: {Path}", _localCachePath);

            // Save to blob storage
            try
            {
                var blobClient = _containerClient.GetBlobClient(BlobName);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                await blobClient.UploadAsync(stream, overwrite: true);
                _logger.LogInformation("Uploaded HaGeZi list to blob storage");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload HaGeZi list to blob storage, continuing with local cache");
            }

            _gamblingDomains = domains;
            _lastUpdate = DateTime.UtcNow;
            _logger.LogInformation("HaGeZi gambling list refreshed: {Count} domains", domains.Count);
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
                _gamblingDomains = ParseAdblockFormat(content);
                _lastUpdate = DateTime.UtcNow;
                _logger.LogInformation("Loaded HaGeZi list from local cache: {Count} domains", _gamblingDomains.Count);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load from local cache");
            }
        }

        // Try blob storage
        try
        {
            var blobClient = _containerClient.GetBlobClient(BlobName);
            var download = await blobClient.DownloadAsync();
            using var sr = new StreamReader(download.Value.Content);
            var content = await sr.ReadToEndAsync();
            
            _gamblingDomains = ParseAdblockFormat(content);
            _lastUpdate = DateTime.UtcNow;
            _logger.LogInformation("Loaded HaGeZi list from blob storage: {Count} domains", _gamblingDomains.Count);
            return;
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
        var domains = new HashSet<string>();

        foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            // Skip comments and empty lines
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("[") || trimmed.StartsWith("!"))
                continue;

            // Extract domain from adblock format (e.g., "||example.com^" becomes "example.com")
            var domain = trimmed
                .Replace("||", string.Empty)
                .Replace("^", string.Empty)
                .Trim();

            if (!string.IsNullOrEmpty(domain) && domain.Contains("."))
            {
                domain = domain.ToLowerInvariant().TrimEnd('.');
                domains.Add(domain);
            }
        }

        return domains;
    }
}

namespace NextDnsBetBlocker.Core.Services;

using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Provider para carregar Tranco List (1M domínios confiáveis) em memória
/// Atualiza 1x por dia, com checkpoint para controle
/// </summary>
public class TrancoAllowlistProvider : ITrancoAllowlistProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ICheckpointStore _checkpointStore;
    private readonly ILogger<TrancoAllowlistProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private HashSet<string> _trancoList = [];
    private DateTime _lastUpdate = DateTime.MinValue;

    private const string BlobNameTrancoAllowlist = "tranco-allowlist.txt";
    private const string TrancoAllowlistUrl = "https://tranco-list.eu/download/JLKKY/full";
    private const string CheckpointKey = "TRANCO_ALLOWLIST_UPDATE";
    private const int UpdateIntervalHours = 24;

    public TrancoAllowlistProvider(
        BlobContainerClient containerClient,
        ICheckpointStore checkpointStore,
        IHttpClientFactory httpClientFactory,
        ILogger<TrancoAllowlistProvider> logger)
    {
        _containerClient = containerClient;
        _checkpointStore = checkpointStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HashSet<string>> GetTrancoDomainsAsync()
    {
        // If cache is fresh (less than 24 hours), return it
        if (_trancoList.Count > 0 && DateTime.UtcNow - _lastUpdate < TimeSpan.FromHours(UpdateIntervalHours))
        {
            return _trancoList;
        }

        // Try to load from blob storage or download fresh
        if (_trancoList.Count == 0)
        {
            await LoadOrRefreshAsync();
        }

        return _trancoList;
    }

    public async Task RefreshAsync()
    {
        _logger.LogInformation("Force refreshing Tranco List from URL");
        await DownloadAndSaveAsync();
    }

    private async Task LoadOrRefreshAsync()
    {
        try
        {
            // Check if we need to refresh (daily)
            var lastCheckpoint = await _checkpointStore.GetLastTimestampAsync(CheckpointKey);

            if (lastCheckpoint.HasValue && DateTime.UtcNow - lastCheckpoint.Value < TimeSpan.FromHours(UpdateIntervalHours))
            {
                _logger.LogInformation("Tranco List checkpoint fresh, loading from blob storage");
                await LoadFromBlobAsync();
                return;
            }

            _logger.LogInformation("Tranco List checkpoint expired or not found, downloading fresh");
            await DownloadAndSaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load/refresh Tranco List, attempting fallback");
            await LoadFromBlobAsync();
        }
    }

    private async Task DownloadAndSaveAsync()
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();

            _logger.LogInformation("Downloading Tranco List from {Url}", TrancoAllowlistUrl);
            var content = await httpClient.GetStringAsync(TrancoAllowlistUrl);

            _logger.LogInformation("Downloaded Tranco List CSV, parsing domains...");
            var domains = ParseTrancoFormat(content);

            _logger.LogInformation("Parsed {Count} domains from Tranco List", domains.Count);

            // Save to blob storage
            try
            {
                var blobClient = _containerClient.GetBlobClient(BlobNameTrancoAllowlist);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                await blobClient.UploadAsync(stream, overwrite: true);
                _logger.LogInformation("Uploaded Tranco List to blob storage");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload Tranco List to blob storage");
            }

            // Update checkpoint
            await _checkpointStore.UpdateLastTimestampAsync(CheckpointKey, DateTime.UtcNow);

            _trancoList = domains;
            _lastUpdate = DateTime.UtcNow;
            _logger.LogInformation("Tranco List refreshed successfully: {Count} domains", domains.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download and save Tranco List");
            throw;
        }
    }

    private async Task LoadFromBlobAsync()
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(BlobNameTrancoAllowlist);
            var download = await blobClient.DownloadAsync();

            using var sr = new StreamReader(download.Value.Content);
            var content = await sr.ReadToEndAsync();

            var domains = ParseTrancoFormat(content);

            _trancoList = domains;
            _lastUpdate = DateTime.UtcNow;
            _logger.LogInformation("Loaded Tranco List from blob storage: {Count} domains", domains.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Tranco List from blob storage");
            _trancoList = [];
        }
    }

    private static HashSet<string> ParseTrancoFormat(string content)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.AsSpan().Trim();

            // Skip comments and empty lines
            if (trimmed.IsEmpty || trimmed[0] == '#' || trimmed[0] == ',')
                continue;

            // Tranco format: rank,domain
            // Extract domain after comma
            var commaIndex = trimmed.IndexOf(',');
            if (commaIndex > 0 && commaIndex < trimmed.Length - 1)
            {
                var domain = trimmed[(commaIndex + 1)..].Trim();

                if (!domain.IsEmpty)
                {
                    domains.Add(domain.ToString().ToLowerInvariant());
                }
            }
        }

        return domains;
    }
}

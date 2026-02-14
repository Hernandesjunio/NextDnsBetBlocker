namespace NextDnsBetBlocker.Worker;

public class WorkerSettings
{
    public string NextDnsApiKey { get; set; } = string.Empty;
    public string NextDnsProfileId { get; set; } = string.Empty;
    public string? AzureStorageConnectionString { get; set; }
    public bool UseBlobStorage { get; set; } = false;
    public int ProcessingIntervalSeconds { get; set; } = 1800;
    public int HageziRefreshIntervalHours { get; set; } = 24;
    public int RateLimitPerSecond { get; set; } = 5;
}

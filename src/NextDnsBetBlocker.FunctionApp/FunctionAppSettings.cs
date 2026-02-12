namespace NextDnsBetBlocker.FunctionApp;

public class FunctionAppSettings
{
    public string NextDnsApiKey { get; set; } = string.Empty;
    public string NextDnsProfileId { get; set; } = string.Empty;
    public string TableStorageUri { get; set; } = string.Empty;
    public string BlobStorageUri { get; set; } = string.Empty;
    public string StorageAccountName { get; set; } = string.Empty;
    public string StorageAccountKey { get; set; } = string.Empty;
    public int RateLimitPerSecond { get; set; } = 5;
}

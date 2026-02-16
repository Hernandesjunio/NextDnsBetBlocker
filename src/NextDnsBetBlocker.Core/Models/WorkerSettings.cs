namespace NextDnsBetBlocker.Core.Models;

/// <summary>
/// Configurações do Worker (Analysis Layer)
/// Inclui NextDNS API, Azure Storage e intervals de processamento
/// Compartilhado entre Core e Worker
/// </summary>
public class WorkerSettings
{
    // NextDNS Configuration
    public string NextDnsApiKey { get; set; } = string.Empty;
    public string NextDnsProfileId { get; set; } = string.Empty;
    public string NextDnsBaseUrl { get; set; } = "https://api.nextdns.io";
    
    // Azure Storage Configuration
    public string? AzureStorageConnectionString { get; set; }
    public bool UseBlobStorage { get; set; } = false;
    
    // Processing Configuration
    public int ProcessingIntervalSeconds { get; set; } = 1800;
    public int HageziRefreshIntervalHours { get; set; } = 24;
    public int RateLimitPerSecond { get; set; } = 5;
}

namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// OBSOLETE: Esta interface não está sendo utilizada na pipeline atual.
/// A obtenção de logs é feita através de ILogsProducer.
/// </summary>
public interface INextDnsClient
{
    /// <summary>
    /// Fetches logs from NextDNS, starting from optional cursor for pagination
    /// Uses 'from' parameter to filter logs since the given timestamp
    /// </summary>
    Task<NextDnsLogsResponse> GetLogsAsync(string profileId, string? cursor = null, int limit = 1000, DateTime? since = null);

    /// <summary>
    /// Fetches logs with optional from/to range filtering
    /// NextDNS API parameters: from (start timestamp), to (end timestamp)
    /// </summary>
    Task<NextDnsLogsResponse> GetLogsRangeAsync(string profileId, string? cursor = null, int limit = 1000, DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// Adds a domain to the denylist
    /// </summary>
    Task<bool> AddToDenylistAsync(string profileId, DenylistBlockRequest request);

    /// <summary>
    /// Adds a domain to the allowlist
    /// </summary>
    Task<bool> AddToAllowlistAsync(string profileId, string domain);
}

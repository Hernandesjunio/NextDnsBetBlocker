namespace NextDnsBetBlocker.Core.Interfaces;

using System.Threading.Channels;
using NextDnsBetBlocker.Core.Models;

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

public interface ICheckpointStore
{
    /// <summary>
    /// Gets the last processed timestamp for a profile
    /// </summary>
    Task<DateTime?> GetLastTimestampAsync(string profileId);

    /// <summary>
    /// Updates the last processed timestamp for a profile
    /// </summary>
    Task UpdateLastTimestampAsync(string profileId, DateTime timestamp);
}

public interface IBlockedDomainStore
{
    /// <summary>
    /// Checks if a domain is already blocked
    /// </summary>
    Task<bool> IsBlockedAsync(string profileId, string domain);

    /// <summary>
    /// Records a domain as blocked
    /// </summary>
    Task MarkBlockedAsync(string profileId, string domain);

    /// <summary>
    /// Gets all blocked domains for a profile
    /// </summary>
    Task<IEnumerable<string>> GetAllBlockedDomainsAsync(string profileId);
}

public interface IHageziProvider
{
    /// <summary>
    /// Gets the HaGeZi Gambling domain list as a HashSet (lowercase, normalized)
    /// </summary>
    Task<HashSet<string>> GetGamblingDomainsAsync();

    /// <summary>
    /// Refreshes the HaGeZi list (called daily)
    /// </summary>
    Task RefreshAsync();
}

public interface IAllowlistProvider
{
    /// <summary>
    /// Gets the local allowlist domains
    /// </summary>
    ISet<string> GetAllowlist();

    /// <summary>
    /// Reloads the allowlist from disk
    /// </summary>
    Task ReloadAsync();
}

public interface IBetClassifier
{
    /// <summary>
    /// Classifies if a domain is a betting/gambling domain
    /// </summary>
    bool IsBetDomain(string domain);
}

public interface IGamblingSuspectStore
{
    /// <summary>
    /// Initialize the table on first access (idempotent)
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Enqueue a domain for analysis
    /// </summary>
    Task EnqueueForAnalysisAsync(string domain);

    /// <summary>
    /// Get pending domains for analysis
    /// </summary>
    Task<IEnumerable<string>> GetPendingDomainsAsync(int limit = 100);

    /// <summary>
    /// Save analysis result
    /// </summary>
    Task SaveAnalysisResultAsync(GamblingSuspect suspect);

    /// <summary>
    /// Check if domain is in whitelist
    /// </summary>
    Task<bool> IsWhitelistedAsync(string domain);

    /// <summary>
    /// Get analysis history for domain
    /// </summary>
    Task<GamblingSuspect?> GetSuspectAsync(string domain);
}

public interface IGamblingSuspectAnalyzer
{
    /// <summary>
    /// Analyze a domain for gambling indicators
    /// </summary>
    Task<AnalysisResult> AnalyzeDomainAsync(string domain);
}

public interface ITrancoAllowlistProvider
{
    
    /// <summary>
    /// Verifica se domínio existe na Tranco List (Table Storage)
    /// Query eficiente com cache 5 minutos
    /// </summary>
    Task<bool> DomainExistsAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force refresh Tranco List from URL
    /// Executa diff import (apenas mudanças)
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna contagem total de domínios na Tranco List
    /// </summary>
    Task<long> GetTotalCountAsync(CancellationToken cancellationToken = default);
}

public interface ITrancoAllowlistConsumer
{
    /// <summary>
    /// Consume suspicious domains and check against Tranco List
    /// If found → allowlist automatically
    /// If not found → forward for detailed analysis
    /// </summary>
    Task StartAsync(
        Channel<LogEntryData> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken);
}

public interface IBetBlockerPipeline
{
    /// <summary>
    /// Runs the complete pipeline: fetch logs, classify, and block domains
    /// </summary>
    Task<BlockerRunStatistics> ProcessLogsAsync(string profileId);

    /// <summary>
    /// Updates the HaGeZi gambling list
    /// </summary>
    Task UpdateHageziAsync();
}

public interface ILogsProducer
{
    /// <summary>
    /// Start producing logs from NextDNS and send to channel
    /// Runs continuously, pulling logs and writing to channel
    /// </summary>
    Task StartAsync(Channel<LogEntryData> channel, string profileId, CancellationToken cancellationToken);
}

public interface IClassifierConsumer
{
    /// <summary>
    /// Consume logs, classify them, and forward only suspicious ones
    /// Filters logs based on allowlist, HaGeZi, and BetClassifier
    /// </summary>
    Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken);
}

public interface IAnalysisConsumer
{
    /// <summary>
    /// Consume suspicious domains and analyze them in detail
    /// Performs HTTP requests and content analysis
    /// </summary>
    Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        string profileId,
        CancellationToken cancellationToken);
}

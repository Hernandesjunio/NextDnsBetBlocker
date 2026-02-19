namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Armazenador de domínios suspeitos de jogo
/// Fila e persistência de análises em andamento
/// </summary>
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

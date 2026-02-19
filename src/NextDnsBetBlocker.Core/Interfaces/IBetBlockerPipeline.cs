namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Pipeline bloqueadora de domínios de apostas
/// Orquestra produção de logs, classificação e análise
/// </summary>
public interface IBetBlockerPipeline
{
    /// <summary>
    /// Runs the complete pipeline: fetch logs, classify, and block domains
    /// </summary>
    Task<BlockerRunStatistics> ProcessLogsAsync(string profileId);

    /// <summary>
    /// Updates the HaGeZi gambling list
    /// </summary>
    [Obsolete("This method is deprecated. The HaGeZi list is now updated automatically on a daily schedule. Manual update is no longer necessary.", true)]
    Task UpdateHageziAsync();
}

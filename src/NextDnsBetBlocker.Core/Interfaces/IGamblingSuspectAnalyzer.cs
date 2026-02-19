namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// OBSOLETE: Esta interface não está sendo utilizada na pipeline atual.
/// Análise detalhada de domínios suspeitos foi removida da pipeline ativa.
/// </summary>
public interface IGamblingSuspectAnalyzer
{
    /// <summary>
    /// Analyze a domain for gambling indicators
    /// </summary>
    Task<AnalysisResult> AnalyzeDomainAsync(string domain);
}

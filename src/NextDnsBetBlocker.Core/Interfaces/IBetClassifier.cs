namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Classificador de domínios de apostas/jogo
/// Analisa padrões em nomes de domínio
/// </summary>
public interface IBetClassifier
{
    /// <summary>
    /// Classifies if a domain is a betting/gambling domain
    /// </summary>
    bool IsBetDomain(string domain);
}

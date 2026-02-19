namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Provedor de allowlist Tranco
/// Verifica domínios contra a Tranco List em cache
/// </summary>
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
    [Obsolete("This method is deprecated. The Tranco List is now updated automatically on a daily schedule. Manual refresh is no longer necessary.", true)]
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna contagem total de domínios na Tranco List
    /// </summary>
    Task<long> GetTotalCountAsync(CancellationToken cancellationToken = default);
}

namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Provedor da lista de domínios de jogo HaGeZi
/// Mantém cache atualizado automaticamente em schedule diário
/// </summary>
public interface IHageziProvider
{
    /// <summary>
    /// Gets the HaGeZi Gambling domain list as a HashSet (lowercase, normalized)
    /// </summary>
    Task<HashSet<string>> GetGamblingDomainsAsync();

    /// <summary>
    /// Refreshes the HaGeZi list (called daily)
    /// </summary>
    [Obsolete("This method is deprecated. The HaGeZi list is now updated automatically on a daily schedule. Manual refresh is no longer necessary.", true)]
    Task RefreshAsync();
}

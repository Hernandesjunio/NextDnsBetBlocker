namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// OBSOLETE: Esta interface não está sendo utilizada na pipeline atual.
/// Armazenamento de domínios bloqueados não é requerido pela pipeline ativa.
/// </summary>
[Obsolete("This interface is not used in the current implementation.", false)]
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

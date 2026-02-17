namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Interface para querying domínios gambling na tabela HageziGambling
/// Usado pelo ClassifierConsumer para filtrar domínios conhecidos
/// </summary>
public interface IHageziGamblingStore
{
    /// <summary>
    /// Verifica se domínio está na lista de gambling do HaGeZi
    /// Testa: exato → wildcards em paralelo (*.xxx.casino)
    /// </summary>
    Task<bool> IsGamblingDomainAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna total de domínios gambling armazenados
    /// </summary>
    Task<int> GetTotalCountAsync();
}

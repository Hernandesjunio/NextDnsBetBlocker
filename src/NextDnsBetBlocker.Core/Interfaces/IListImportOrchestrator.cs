namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Orquestrador da importação
/// Coordena processamento e inserção paralela de domínios
/// Executa operações de Add (Upsert) ou Remove (Delete) em paralelo
/// </summary>
public interface IListImportOrchestrator
{
    /// <summary>
    /// Executa operação de importação com domínios já baixados
    /// Responsável por paralelização, batching, rate limiting e resiliência
    /// </summary>
    /// <param name="config">Configuração da lista a ser importada</param>
    /// <param name="operationType">Tipo de operação (Add/Upsert ou Remove/Delete)</param>
    /// <param name="domains">Domínios já baixados/processados (não faz download)</param>
    /// <param name="progress">Reporter de progresso em tempo real</param>
    /// <param name="cancellationToken">Token para cancelamento</param>
    /// <returns>Métricas finais da importação</returns>
    Task<ImportMetrics> ExecuteImportAsync(
        ListImportItemConfig config,
        ImportOperationType operationType,
        IEnumerable<string> domains,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// [OBSOLETE] Executa importação com produtor integrado (deprecated)
    /// Use a nova sobrecarga que passa domínios já baixados
    /// </summary>
    [Obsolete("Use ExecuteImportAsync(config, operationType, domains, progress, cancellationToken) instead", true)]
    Task<ImportMetrics> ExecuteImportAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken);
}

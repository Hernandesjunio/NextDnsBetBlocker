namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Importador genérico para qualquer lista de domínios
/// Coordena todo o processo
/// </summary>
public interface IListImporter
{
    /// <summary>
    /// Executa importação completa de uma lista
    /// Lida com streaming, batching, rate limiting, resiliência
    /// </summary>
    Task<ImportMetrics> ImportAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executa diff entre arquivo anterior e novo
    /// Insere apenas mudanças (adds/removes)
    /// Mais eficiente que re-importar tudo
    /// </summary>
    Task<ImportMetrics> ImportDiffAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken);
}

namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Repositório para armazenar/recuperar arquivos do blob
/// Mantém referência do último arquivo importado
/// </summary>
public interface IListBlobRepository
{
    /// <summary>
    /// Salva o arquivo de importação no blob
    /// </summary>
    Task<string> SaveImportFileAsync(
        string containerName,
        string blobName,
        Stream fileStream,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recupera arquivo anterior do blob
    /// </summary>
    Task<Stream?> GetPreviousImportFileAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Salva metadados sobre a importação
    /// </summary>
    Task SaveImportMetadataAsync(
        string containerName,
        string metadataName,
        ImportedListMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recupera metadados de importação anterior
    /// </summary>
    Task<ImportedListMetadata?> GetImportMetadataAsync(
        string containerName,
        string metadataName,
        CancellationToken cancellationToken);
}

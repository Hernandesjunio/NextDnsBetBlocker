namespace NextDnsBetBlocker.Core.Services.Import;

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Text.Json;

/// <summary>
/// Repositório para armazenar/recuperar arquivos do Blob Storage
/// Persiste último arquivo importado para diffs futuros
/// </summary>
public class ListBlobRepository : IListBlobRepository
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<ListBlobRepository> _logger;

    public ListBlobRepository(
        string connectionString,
        string containerName,
        ILogger<ListBlobRepository> logger)
    {
        _logger = logger;
        
        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    public ListBlobRepository(
        BlobContainerClient containerClient,
        ILogger<ListBlobRepository> logger)
    {
        _containerClient = containerClient;
        _logger = logger;
    }

    public async Task<string> SaveImportFileAsync(
        string containerName,
        string blobName,
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Saving import file to blob: {BlobName}", blobName);

            var blobClient = _containerClient.GetBlobClient(blobName);

            // Upload com metadata
            var metadata = new Dictionary<string, string>
            {
                { "ImportedAt", DateTime.UtcNow.ToString("O") }
            };

            await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken: cancellationToken);

            // Atualizar metadata após upload
            await blobClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);

            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var blobUri = blobClient.Uri.ToString();

            _logger.LogInformation(
                "Import file saved successfully: {BlobName}, Size: {Size} bytes",
                blobName,
                properties.Value.ContentLength);

            return blobUri;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save import file: {BlobName}", blobName);
            throw;
        }
    }

    public async Task<Stream?> GetPreviousImportFileAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retrieving previous import file from blob: {BlobName}", blobName);

            var blobClient = _containerClient.GetBlobClient(blobName);

            // Verificar se existe
            var exists = await blobClient.ExistsAsync(cancellationToken);
            if (!exists.Value)
            {
                _logger.LogDebug("Previous import file not found: {BlobName}", blobName);
                return null;
            }

            // Download para MemoryStream
            var download = await blobClient.DownloadAsync(cancellationToken);
            var memoryStream = new MemoryStream();
            await download.Value.Content.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Seek(0, SeekOrigin.Begin);

            _logger.LogInformation(
                "Previous import file retrieved: {BlobName}, Size: {Size} bytes",
                blobName,
                memoryStream.Length);

            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve previous import file: {BlobName}", blobName);
            return null;
        }
    }

    public async Task SaveImportMetadataAsync(
        string containerName,
        string metadataName,
        ImportedListMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Saving import metadata to blob: {MetadataName}", metadataName);

            var blobClient = _containerClient.GetBlobClient(metadataName);

            // Serializar metadata
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Import metadata saved successfully: {MetadataName}, ListName: {ListName}, Records: {Records}",
                metadataName,
                metadata.ListName,
                metadata.RecordCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save import metadata: {MetadataName}", metadataName);
            throw;
        }
    }

    public async Task<ImportedListMetadata?> GetImportMetadataAsync(
        string containerName,
        string metadataName,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retrieving import metadata from blob: {MetadataName}", metadataName);

            var blobClient = _containerClient.GetBlobClient(metadataName);

            // Verificar se existe
            var exists = await blobClient.ExistsAsync(cancellationToken);
            if (!exists.Value)
            {
                _logger.LogDebug("Import metadata not found: {MetadataName}", metadataName);
                return null;
            }

            // Download
            var download = await blobClient.DownloadAsync(cancellationToken);
            using (var streamReader = new StreamReader(download.Value.Content))
            {
                var json = await streamReader.ReadToEndAsync();
                var metadata = JsonSerializer.Deserialize<ImportedListMetadata>(json);

                _logger.LogInformation(
                    "Import metadata retrieved: {MetadataName}, ListName: {ListName}, ImportedAt: {ImportedAt}",
                    metadataName,
                    metadata?.ListName,
                    metadata?.ImportedAt);

                return metadata;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve import metadata: {MetadataName}", metadataName);
            return null;
        }
    }

    /// <summary>
    /// Cria container se não existir
    /// </summary>
    public async Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _logger.LogInformation("Blob container ensured: {ContainerName}", _containerClient.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure blob container exists");
            throw;
        }
    }
}

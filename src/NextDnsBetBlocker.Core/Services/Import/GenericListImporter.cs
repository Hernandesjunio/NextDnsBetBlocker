namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Importador genérico para qualquer lista de domínios
/// Coordena todo o processo de importação e diffs
/// Reutilizável para múltiplas fontes (Tranco, Hagezi, etc)
/// </summary>
public class GenericListImporter : IListImporter
{
    private readonly ILogger<GenericListImporter> _logger;
    private readonly IListImportOrchestrator _orchestrator;
    private readonly IListBlobRepository _blobRepository;

    public GenericListImporter(
        ILogger<GenericListImporter> logger,
        IListImportOrchestrator orchestrator,
        IListBlobRepository blobRepository)
    {
        _logger = logger;
        _orchestrator = orchestrator;
        _blobRepository = blobRepository;
    }

    /// <summary>
    /// Executa importação completa de uma lista
    /// Lida com streaming, batching, rate limiting, resiliência
    /// Persiste arquivo no blob após sucesso
    /// </summary>
    public async Task<ImportMetrics> ImportAsync(
        ListImportConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting full import for {ListName} from {SourceUrl}",
                config.ListName,
                config.SourceUrl);

            // Executar importação
            var metrics = await _orchestrator.ExecuteImportAsync(config, progress, cancellationToken);

            // Se bem-sucedido, salvar arquivo no blob
            if (metrics.TotalErrors == 0)
            {
                await SaveImportedFileAsync(config, cancellationToken);
            }

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Full import failed for {ListName}", config.ListName);
            throw;
        }
    }

    /// <summary>
    /// Executa diff entre arquivo anterior e novo
    /// Insere apenas mudanças (adds/removes)
    /// Mais eficiente que re-importar tudo
    /// </summary>
    public async Task<ImportMetrics> ImportDiffAsync(
        ListImportConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting diff import for {ListName} from {SourceUrl}",
                config.ListName,
                config.SourceUrl);

            // TODO: Implementar lógica de diff
            // 1. Baixar arquivo anterior do blob
            // 2. Fazer diff local em memória
            // 3. Enviar apenas mudanças
            
            throw new NotImplementedException("Diff import will be implemented in Onda 3");
        }
        catch (NotImplementedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diff import failed for {ListName}", config.ListName);
            throw;
        }
    }

    private async Task SaveImportedFileAsync(ListImportConfig config, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Saving imported file metadata for {ListName}",
                config.ListName);

            var metadata = new ImportedListMetadata
            {
                ListName = config.ListName,
                FileHash = GenerateRandomHash(), // TODO: Usar hash real do arquivo
                RecordCount = 0, // TODO: Usar contagem real
                FileSizeBytes = 0, // TODO: Usar tamanho real
                SourceVersion = "1.0" // TODO: Extrair de metadados da origem
            };

            var blobName = $"{config.ListName.ToLowerInvariant()}/latest";
            var metadataName = $"{config.ListName.ToLowerInvariant()}/metadata.json";

            await _blobRepository.SaveImportMetadataAsync(
                config.BlobContainer,
                metadataName,
                metadata,
                cancellationToken);

            _logger.LogInformation(
                "Imported file metadata saved for {ListName}",
                config.ListName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save imported file metadata for {ListName}", config.ListName);
            // Não lançar exceção - importação foi bem-sucedida, salvamento de backup é secundário
        }
    }

    private static string GenerateRandomHash()
    {
        return Guid.NewGuid().ToString("N")[..16];
    }
}

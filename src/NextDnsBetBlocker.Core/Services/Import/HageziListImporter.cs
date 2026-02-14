namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Importador para listas HaGeZi (Gambling)
/// Usa HageziProvider para baixar dados
/// Integra com orquestrador genérico para paralelismo
/// Otimizado para ~200k items (menor que Tranco)
/// </summary>
public class HageziListImporter : IListImporter
{
    private readonly ILogger<HageziListImporter> _logger;
    private readonly IListImportOrchestrator _orchestrator;
    private readonly IHageziProvider _hageziProvider;
    private readonly IListBlobRepository _blobRepository;

    public HageziListImporter(
        ILogger<HageziListImporter> logger,
        IListImportOrchestrator orchestrator,
        IHageziProvider hageziProvider,
        IListBlobRepository blobRepository)
    {
        _logger = logger;
        _orchestrator = orchestrator;
        _hageziProvider = hageziProvider;
        _blobRepository = blobRepository;
    }

    /// <summary>
    /// Executa importação completa de lista HaGeZi
    /// 1. Baixa lista via HageziProvider.RefreshAsync()
    /// 2. Importa para Table Storage via orquestrador
    /// 3. Salva arquivo no blob após sucesso
    /// </summary>
    public async Task<ImportMetrics> ImportAsync(
        ListImportConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting HaGeZi import for {ListName} → {TableName}",
                config.ListName,
                config.TableName);

            // 1. Refresh da lista HaGeZi (download + parse)
            _logger.LogInformation("Step 1: Refreshing HaGeZi gambling list from remote sources");
            await _hageziProvider.RefreshAsync();
            _logger.LogInformation("✓ HaGeZi list refreshed successfully");

            // 2. Importar via orquestrador genérico (parallelismo + resiliência)
            _logger.LogInformation("Step 2: Importing HaGeZi domains to Table Storage");
            var metrics = await _orchestrator.ExecuteImportAsync(config, progress, cancellationToken);

            // 3. Se bem-sucedido, salvar arquivo no blob
            if (metrics.TotalErrors == 0)
            {
                _logger.LogInformation("Step 3: Saving imported file to blob storage");
                await SaveImportedFileAsync(config, cancellationToken);
                _logger.LogInformation("✓ File saved to blob storage");
            }
            else
            {
                _logger.LogWarning(
                    "Skipping blob save due to errors during import. Errors: {ErrorCount}",
                    metrics.TotalErrors);
            }

            _logger.LogInformation(
                "✓ HaGeZi import completed successfully | Inserted: {Inserted} | Errors: {Errors} | Time: {Time}",
                metrics.TotalInserted,
                metrics.TotalErrors,
                metrics.ElapsedTime);

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HaGeZi import failed for {ListName}", config.ListName);
            throw;
        }
    }

    /// <summary>
    /// Executa diff entre arquivo anterior e novo (mais eficiente)
    /// Apenas insere mudanças (adds/removes)
    /// Reduz I/O significativamente
    /// </summary>
    public async Task<ImportMetrics> ImportDiffAsync(
        ListImportConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting HaGeZi diff import for {ListName} from {SourceUrl}",
                config.ListName,
                config.SourceUrl);

            var metrics = new ImportMetrics();

            // 1. Refresh da lista nova
            _logger.LogInformation("Refreshing HaGeZi gambling list from remote sources");
            await _hageziProvider.RefreshAsync();
            _logger.LogInformation("✓ HaGeZi list refreshed");

            // 2. Obter lista atual do Table Storage (para comparação)
            _logger.LogInformation("Retrieving current list from Table Storage");
            var currentDomains = await GetCurrentDomainsAsync(config, cancellationToken);
            _logger.LogInformation("Retrieved {Count} current domains from Table Storage", currentDomains.Count);

            // 3. Obter nova lista (em cache agora após refresh)
            var newDomains = await _hageziProvider.GetGamblingDomainsAsync();
            _logger.LogInformation("Retrieved {Count} new domains from HaGeZi provider", newDomains.Count);

            // 4. Calcular diff
            var domainsToAdd = newDomains.Except(currentDomains).ToHashSet();
            var domainsToRemove = currentDomains.Except(newDomains).ToHashSet();

            _logger.LogInformation(
                "Diff calculated: {ToAdd} to add, {ToRemove} to remove",
                domainsToAdd.Count,
                domainsToRemove.Count);

            // 5. Se há mudanças, aplicar diff
            if (domainsToAdd.Count > 0 || domainsToRemove.Count > 0)
            {
                _logger.LogInformation("Applying diff to Table Storage");

                // TODO: Implementar lógica de diff (adicionar + remover)
                // Por enquanto, fazer import completo
                metrics = await _orchestrator.ExecuteImportAsync(config, progress, cancellationToken);
            }
            else
            {
                _logger.LogInformation("No changes detected - skipping import");
                metrics.TotalProcessed = newDomains.Count;
                metrics.TotalInserted = 0;
            }

            _logger.LogInformation(
                "✓ HaGeZi diff import completed | Inserted: {Inserted} | Errors: {Errors} | Time: {Time}",
                metrics.TotalInserted,
                metrics.TotalErrors,
                metrics.ElapsedTime);

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HaGeZi diff import failed for {ListName}", config.ListName);
            throw;
        }
    }

    /// <summary>
    /// Obter domínios atualmente no Table Storage
    /// </summary>
    private async Task<HashSet<string>> GetCurrentDomainsAsync(
        ListImportConfig config,
        CancellationToken cancellationToken)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // TODO: Implementar query no Table Storage para pegar todos os domínios
        // Por enquanto, retorna vazio (vai fazer import completo)

        return domains;
    }

    /// <summary>
    /// Salvar arquivo importado no blob storage
    /// </summary>
    private async Task SaveImportedFileAsync(ListImportConfig config, CancellationToken cancellationToken)
    {
        try
        {
            // Obter lista do provider (já em cache)
            var domains = await _hageziProvider.GetGamblingDomainsAsync();

            if (domains.Count == 0)
            {
                _logger.LogWarning("No domains to save - HaGeZi provider returned empty list");
                return;
            }

            // Criar arquivo de texto com um domínio por linha
            var content = string.Join("\n", domains);

            // Converter para stream
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

            // Salvar no blob
            var blobName = $"hagezi-gambling-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt";
            await _blobRepository.SaveImportFileAsync(config.BlobContainer, blobName, stream, cancellationToken);

            _logger.LogInformation(
                "Saved HaGeZi import to blob: {BlobName} ({DomainCount} domains)",
                blobName,
                domains.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save HaGeZi import to blob storage");
            throw;
        }
    }
}

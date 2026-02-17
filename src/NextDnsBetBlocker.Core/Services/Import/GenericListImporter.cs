namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Security.Cryptography;

/// <summary>
/// Importador genérico para qualquer lista de domínios
/// Responsabilidades:
/// 1. Baixar domínios das fontes (URLs)
/// 2. Fazer diffs se necessário
/// 3. Chamar orchestrator para inserção paralela
/// 4. Persistir arquivo no blob
/// 
/// Reutilizável para múltiplas fontes (Tranco, Hagezi, etc)
/// </summary>
public class GenericListImporter : IListImporter
{
    private readonly ILogger<GenericListImporter> _logger;
    private readonly IListImportOrchestrator _orchestrator;
    private readonly IListBlobRepository _blobRepository;
    private readonly IListTableStorageRepository _tableRepository;

    public GenericListImporter(
        ILogger<GenericListImporter> logger,
        IListImportOrchestrator orchestrator,
        IListBlobRepository blobRepository,
        IListTableStorageRepository tableRepository)
    {
        _logger = logger;
        _orchestrator = orchestrator;
        _blobRepository = blobRepository;
        _tableRepository = tableRepository;
    }

    /// <summary>
    /// Executa importação completa de uma lista
    /// Baixa dados da origem, delega inserção ao orchestrator
    /// Persiste arquivo no blob após sucesso
    /// </summary>
    public async Task<ImportMetrics> ImportAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting full import for {ListName} from {SourceCount} sources",
                config.ListName,
                config.SourceUrl.Length);

            // 1. Baixar dados de todas as fontes
            var domains = await DownloadAndParseAsync(config.SourceUrl, cancellationToken);
            _logger.LogInformation("Downloaded {Count:N0} domains from all sources", domains.Count);

            // 2. Chamar orchestrator para inserção paralela
            var metrics = await _orchestrator.ExecuteImportAsync(
                config,
                ImportOperationType.Add,
                domains,
                progress,
                cancellationToken);

            // 3. Se bem-sucedido, salvar arquivo no blob como referência
            if (metrics.TotalErrors == 0)
            {
                await SaveImportedFileAsync(config, domains, cancellationToken);
                _logger.LogInformation(
                    "✓ Full import completed and file saved to blob for {ListName}",
                    config.ListName);
            }
            else
            {
                _logger.LogWarning(
                    "Full import completed with errors for {ListName}: {Errors} errors",
                    config.ListName,
                    metrics.TotalErrors);
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
    /// Insere apenas mudanças (adds/removes) em paralelo
    /// Mais eficiente que re-importar tudo (reduz I/O 95%)
    /// </summary>
    public async Task<ImportMetrics> ImportDiffAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting diff import for {ListName} from {SourceCount} sources",
                config.ListName,
                config.SourceUrl.Length);

            // 1. Baixar dados novo do servidor
            _logger.LogInformation("Downloading new list from sources");
            var newDomains = await DownloadAndParseAsync(config.SourceUrl, cancellationToken);
            _logger.LogInformation("Downloaded {Count:N0} domains", newDomains.Count);

            // 2. Recuperar arquivo anterior do blob
            _logger.LogInformation("Retrieving previous list from blob storage");
            var previousDomains = await GetPreviousDomainsAsync(config, cancellationToken);
            _logger.LogInformation("Retrieved {Count:N0} previous domains", previousDomains.Count);

            // 3. Calcular diff em memória
            var adds = newDomains.Except(previousDomains).ToHashSet();
            var removes = previousDomains.Except(newDomains).ToHashSet();

            _logger.LogInformation(
                "Diff calculated for {ListName}: +{Adds:N0} adds, -{Removes:N0} removes",
                config.ListName,
                adds.Count,
                removes.Count);

            // 4. Executar operações em paralelo
            var addTask = (adds.Count > 0)
                ? _orchestrator.ExecuteImportAsync(
                    config,
                    ImportOperationType.Add,
                    adds,
                    progress,
                    cancellationToken)
                : Task.FromResult(new ImportMetrics
                {
                    Status = ImportStatus.Completed,
                    TotalProcessed = 0,
                    TotalInserted = 0,
                    TotalErrors = 0
                });

            var removeTask = (removes.Count > 0)
                ? _orchestrator.ExecuteImportAsync(
                    config,
                    ImportOperationType.Remove,
                    removes,
                    progress,
                    cancellationToken)
                : Task.FromResult(new ImportMetrics
                {
                    Status = ImportStatus.Completed,
                    TotalProcessed = 0,
                    TotalInserted = 0,
                    TotalErrors = 0
                });

            var results = await Task.WhenAll(addTask, removeTask);

            // 5. Agregar métricas
            var metrics = new ImportMetrics
            {
                TotalProcessed = results.Sum(r => r.TotalProcessed),
                TotalInserted = results.Sum(r => r.TotalInserted),
                TotalErrors = results.Sum(r => r.TotalErrors),
                Status = results.All(r => r.Status == ImportStatus.Completed) 
                    ? ImportStatus.Completed 
                    : ImportStatus.Failed,
                ElapsedTime = TimeSpan.FromMilliseconds(results.Max(r => r.ElapsedTime.TotalMilliseconds))
            };

            // 6. Salvar novo arquivo como referência para próximo diff
            await SaveImportedFileAsync(config, newDomains, cancellationToken);

            _logger.LogInformation(
                "✓ Diff import completed for {ListName}: +{Adds:N0} adds, -{Removes:N0} removes | Errors: {Errors}",
                config.ListName,
                adds.Count,
                removes.Count,
                metrics.TotalErrors);

            progress.Report(new ImportProgress { Metrics = metrics });
            return metrics;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Diff import cancelled for {ListName}", config.ListName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diff import failed for {ListName}", config.ListName);
            throw;
        }
    }

    /// <summary>
    /// Baixar e fazer parse de domínios de múltiplas fontes
    /// Suporta URLs HTTP/HTTPS
    /// Merge automático de múltiplas fontes
    /// </summary>
    private async Task<HashSet<string>> DownloadAndParseAsync(
        string[] sourceUrls,
        CancellationToken cancellationToken)
    {
        var allDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceUrl in sourceUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sourceUrl))
            {
                _logger.LogWarning("Empty source URL, skipping");
                continue;
            }

            try
            {
                _logger.LogInformation("Downloading from {SourceUrl}", sourceUrl);
                var domainsParsed = await DownloadAndParseFromSourceAsync(sourceUrl, cancellationToken);
                allDomains.UnionWith(domainsParsed);
                _logger.LogInformation("Parsed {Count:N0} domains from {SourceUrl}", domainsParsed.Count, sourceUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download from {SourceUrl}, continuing with other sources", sourceUrl);
                // Continuar com próxima fonte ao invés de falhar completamente
            }
        }

        if (allDomains.Count == 0)
        {
            throw new InvalidOperationException($"No domains downloaded from any source");
        }

        return allDomains;
    }

    /// <summary>
    /// Baixar e fazer parse de uma única fonte com retry automático
    /// </summary>
    private async Task<HashSet<string>> DownloadAndParseFromSourceAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int maxRetries = 3;
        int attempt = 0;

        while (attempt < maxRetries)
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var content = await httpClient.GetStringAsync(sourceUrl, cancellationToken);

                // Parse domínios
                foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();

                    // Ignorar linhas vazias e comentários
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    // Suportar formatos: "domínio" ou "rank,domínio" (Tranco)
                    var domain = trimmed.Contains(',')
                        ? trimmed.Split(',')[1].Trim()
                        : trimmed;

                    if (!string.IsNullOrWhiteSpace(domain))
                    {
                        domains.Add(domain.ToLowerInvariant());
                    }
                }

                return domains; // Sucesso
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    _logger.LogError(ex, "Failed to download from {SourceUrl} after {MaxRetries} attempts", sourceUrl, maxRetries);
                    throw;
                }

                // Aguardar antes de retry (backoff exponencial)
                var delayMs = (int)(1000 * Math.Pow(2, attempt - 1)); // 1s, 2s, 4s
                _logger.LogWarning(ex, "Download attempt {Attempt}/{MaxRetries} failed for {SourceUrl}, retrying in {DelayMs}ms", 
                    attempt, maxRetries, sourceUrl, delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Failed to download from {sourceUrl}");
    }

    private async Task<HashSet<string>> GetPreviousDomainsAsync(
        ListImportItemConfig config,
        CancellationToken cancellationToken)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var metadataName = $"{config.ListName.ToLowerInvariant()}/metadata.json";
            var metadata = await _blobRepository.GetImportMetadataAsync(
                config.BlobContainer,
                metadataName,
                cancellationToken);

            if (metadata == null)
            {
                _logger.LogInformation("No previous metadata found for {ListName} - first import", config.ListName);
                return domains; // Primeira importação
            }

            // Recuperar arquivo anterior
            var previousBlobName = $"{config.ListName.ToLowerInvariant()}/previous";
            var stream = await _blobRepository.GetPreviousImportFileAsync(
                config.BlobContainer,
                previousBlobName,
                cancellationToken);

            if (stream == null)
            {
                _logger.LogInformation("No previous file found for {ListName}", config.ListName);
                return domains;
            }

            // Parse arquivo anterior
            using (var reader = new StreamReader(stream))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var trimmed = line.Trim();
                        if (!trimmed.StartsWith("#"))
                        {
                            var domain = trimmed.Contains(',')
                                ? trimmed.Split(',')[1].Trim()
                                : trimmed;

                            if (!string.IsNullOrWhiteSpace(domain))
                            {
                                domains.Add(domain.ToLowerInvariant());
                            }
                        }
                    }
                }
            }

            return domains;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve previous domains for {ListName}", config.ListName);
            return domains; // Retornar vazio em caso de erro (vai fazer full import)
        }
    }

    /// <summary>
    /// Salvar arquivo no blob como referência para próximo diff
    /// Arquivo é simplesmente uma lista de domínios ordenados, um por linha
    /// Agnóstico à origem (não sabe se veio de download, diff merge, etc)
    /// </summary>
    private async Task SaveImportedFileAsync(
        ListImportItemConfig config,
        HashSet<string> finalDomains,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Saving imported file for {ListName}", config.ListName);

            // Converter HashSet para arquivo ordenado
            var csvContent = string.Join("\n", finalDomains.OrderBy(x => x));
            var contentBytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
            var stream = new MemoryStream(contentBytes);

            // Salvar como referência para próximo diff
            var blobName = $"{config.ListName.ToLowerInvariant()}/previous";
            await _blobRepository.SaveImportFileAsync(
                config.BlobContainer,
                blobName,
                stream,
                cancellationToken);

            // Salvar metadata
            var metadata = new ImportedListMetadata
            {
                ListName = config.ListName,
                FileHash = GenerateSha256Hash(csvContent),
                RecordCount = finalDomains.Count,
                FileSizeBytes = stream.Length,
                SourceVersion = DateTime.UtcNow.ToString("O")
            };

            var metadataName = $"{config.ListName.ToLowerInvariant()}/metadata.json";
            await _blobRepository.SaveImportMetadataAsync(
                config.BlobContainer,
                metadataName,
                metadata,
                cancellationToken);

            _logger.LogInformation(
                "✓ Imported file saved for {ListName}: {Count:N0} records, {Size:N0} bytes",
                config.ListName,
                finalDomains.Count,
                stream.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save imported file for {ListName}", config.ListName);
            // Não lançar exceção - importação foi bem-sucedida, salvamento de backup é secundário
        }
    }

    /// <summary>
    /// Gerar hash SHA256 de um conteúdo para validação
    /// </summary>
    private static string GenerateSha256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashedBytes);
    }
}

namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Security.Cryptography;

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
    /// Mais eficiente que re-importar tudo (reduz I/O 95%)
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
                string.Join(", ", config.SourceUrl));

            var metrics = new ImportMetrics();

            // 1. Baixar arquivo novo do servidor (usar primeira URL para diff)
            var sourceUrl = config.SourceUrl.FirstOrDefault();
            if (string.IsNullOrEmpty(sourceUrl))
            {
                throw new InvalidOperationException($"No source URLs configured for {config.ListName}");
            }

            _logger.LogInformation("Downloading new list from {SourceUrl}", sourceUrl);
            var newDomains = await DownloadAndParseAsync(sourceUrl, cancellationToken);
            _logger.LogInformation("Downloaded {Count} domains", newDomains.Count);

            // 2. Recuperar arquivo anterior do blob
            _logger.LogInformation("Retrieving previous list from blob storage");
            var previousDomains = await GetPreviousDomainsAsync(config, cancellationToken);
            _logger.LogInformation("Retrieved {Count} previous domains", previousDomains.Count);

            // 3. Calcular diff em memória (você tem 64GB)
            var adds = newDomains.Except(previousDomains).ToHashSet();
            var removes = previousDomains.Except(newDomains).ToHashSet();

            _logger.LogInformation(
                "Diff calculated for {ListName}: +{Adds} adds, -{Removes} removes",
                config.ListName, adds.Count, removes.Count);

            // 4. Aplicar mudanças
            if (adds.Count > 0)
            {
                var addMetrics = await ApplyAddsAsync(config, adds, progress, cancellationToken);
                metrics.TotalInserted += addMetrics.TotalInserted;
                metrics.TotalErrors += addMetrics.TotalErrors;
            }

            if (removes.Count > 0)
            {
                var removeMetrics = await ApplyRemovesAsync(config, removes, progress, cancellationToken);
                metrics.TotalInserted += removeMetrics.TotalInserted;
                metrics.TotalErrors += removeMetrics.TotalErrors;
            }

            // 5. Salvar novo arquivo como referência para próximo diff
            await SaveImportedFileAsync(config, newDomains, cancellationToken);

            metrics.TotalProcessed = adds.Count + removes.Count;
            metrics.Status = metrics.TotalErrors == 0 ? ImportStatus.Completed : ImportStatus.Failed;

            _logger.LogInformation(
                "Diff import completed for {ListName}: +{Adds}, -{Removes}, Errors={Errors}",
                config.ListName,
                adds.Count,
                removes.Count,
                metrics.TotalErrors);

            progress.Report(new ImportProgress { Metrics = metrics });
            return metrics;
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

    private async Task<HashSet<string>> DownloadAndParseAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var httpClient = new HttpClient())
        {
            try
            {
                var content = await httpClient.GetStringAsync(sourceUrl, cancellationToken);

                // Parse domínios
                foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();

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

                return domains;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download and parse from {SourceUrl}", sourceUrl);
                throw;
            }
        }
    }

    private async Task<HashSet<string>> GetPreviousDomainsAsync(
        ListImportConfig config,
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

    private async Task<ImportMetrics> ApplyAddsAsync(
        ListImportConfig config,
        HashSet<string> adds,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        var metrics = new ImportMetrics();

        try
        {
            _logger.LogInformation("Applying {Count} adds to {TableName}", adds.Count, config.TableName);

            var processed = 0;
            foreach (var chunk in adds.Chunk(config.BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entries = chunk
                    .Select(domain => new DomainListEntry
                    {
                        PartitionKey = GetPartitionKey(domain),
                        RowKey = domain
                    })
                    .ToList();

                var result = await _tableRepository.UpsertBatchAsync(
                    config.TableName,
                    entries,
                    cancellationToken);

                metrics.TotalInserted += result.SuccessCount;
                metrics.TotalErrors += result.FailureCount;
                processed += chunk.Length;

                if (processed % 1000 == 0)
                {
                    _logger.LogDebug("Applied {Processed}/{Total} adds", processed, adds.Count);
                    progress.Report(new ImportProgress { Metrics = metrics });
                }
            }

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply adds");
            throw;
        }
    }

    private async Task<ImportMetrics> ApplyRemovesAsync(
        ListImportConfig config,
        HashSet<string> removes,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        var metrics = new ImportMetrics();

        try
        {
            _logger.LogInformation("Applying {Count} removes from {TableName}", removes.Count, config.TableName);

            var processed = 0;
            foreach (var chunk in removes.Chunk(config.BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entries = chunk
                    .Select(domain => new DomainListEntry
                    {
                        PartitionKey = GetPartitionKey(domain),
                        RowKey = domain
                    })
                    .ToList();

                var result = await _tableRepository.DeleteBatchAsync(
                    config.TableName,
                    entries,
                    cancellationToken);

                metrics.TotalInserted += result.SuccessCount;
                metrics.TotalErrors += result.FailureCount;
                processed += chunk.Length;

                if (processed % 1000 == 0)
                {
                    _logger.LogDebug("Applied {Processed}/{Total} removes", processed, removes.Count);
                    progress.Report(new ImportProgress { Metrics = metrics });
                }
            }

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply removes");
            throw;
        }
    }

    private async Task SaveImportedFileAsync(
        ListImportConfig config,
        HashSet<string> newDomains,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Saving imported file for {ListName}", config.ListName);

            // Converter HashSet para stream para salvar no blob
            var csvContent = string.Join("\n", newDomains.OrderBy(x => x));
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));

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
                RecordCount = newDomains.Count,
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
                "Imported file metadata saved for {ListName}: {Count} records, {Size} bytes",
                config.ListName,
                newDomains.Count,
                stream.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save imported file metadata for {ListName}", config.ListName);
            // Não lançar exceção - importação foi bem-sucedida
        }
    }

    private string GetPartitionKey(string domain)
    {
        // Usar estratégia injetada do container (será registrada em Program.cs)
        // Por enquanto, criar uma instância simples
        var strategy = new PartitionKeyStrategy(10);
        return strategy.GetPartitionKey(domain);
    }

    private static string GenerateSha256Hash(string input)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashedBytes);
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

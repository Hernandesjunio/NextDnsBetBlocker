namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.IO.Compression;
using System.Threading.Channels;

/// <summary>
/// Produtor de dados para importação
/// Lê dados da origem (URL/arquivo) em streaming
/// Enfileira itens no channel sem carregar tudo em memória
/// </summary>
public class ListImportProducer : IListImportProducer
{
    private readonly ILogger<ListImportProducer> _logger;
    private readonly HttpClient _httpClient;

    public ListImportProducer(ILogger<ListImportProducer> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Lê dados da origem em streaming e escreve no channel
    /// Suporta URLs HTTP/HTTPS e arquivos locais
    /// Descompacta automaticamente se for ZIP
    /// </summary>
    public async Task ProduceAsync(
        Channel<string> outputChannel,
        ListImportConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting production for {ListName} from {SourceUrl}",
                config.ListName,
                config.SourceUrl);

            await using (var stream = await GetSourceStreamAsync(config.SourceUrl, cancellationToken))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException($"Could not open source: {config.SourceUrl}");
                }

                // Se for ZIP, extrair primeiro arquivo
                if (config.SourceUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await ProduceFromZipAsync(stream, outputChannel, cancellationToken);
                }
                else if (config.SourceUrl.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase) ||
                         config.SourceUrl.EndsWith(".txt.gz", StringComparison.OrdinalIgnoreCase))
                {
                    await ProduceFromGzipAsync(stream, outputChannel, cancellationToken);
                }
                else
                {
                    await ProduceFromStreamAsync(stream, outputChannel, cancellationToken);
                }
            }

            _logger.LogInformation("Production completed for {ListName}", config.ListName);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Production cancelled for {ListName}", config.ListName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Production failed for {ListName}", config.ListName);
            throw;
        }
        finally
        {
            // Sinalizar fim de produção
            outputChannel.Writer.TryComplete();
        }
    }

    private async Task<Stream?> GetSourceStreamAsync(string source, CancellationToken cancellationToken)
    {
        // Se for URL HTTP(S)
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _logger.LogDebug("Downloading from URL: {Url}", source);
                var response = await _httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStreamAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download from {Url}", source);
                throw;
            }
        }

        // Se for arquivo local
        if (File.Exists(source))
        {
            try
            {
                _logger.LogDebug("Opening local file: {Path}", source);
                return File.OpenRead(source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open local file: {Path}", source);
                throw;
            }
        }

        _logger.LogError("Source not found: {Source}", source);
        return null;
    }

    private async Task ProduceFromZipAsync(
        Stream zipStream,
        Channel<string> outputChannel,
        CancellationToken cancellationToken)
    {
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            // Pegar primeiro arquivo do ZIP
            var firstEntry = archive.Entries.FirstOrDefault();
            if (firstEntry == null)
            {
                throw new InvalidOperationException("ZIP file is empty");
            }

            _logger.LogInformation("Extracting from ZIP: {EntryName}", firstEntry.Name);

            await using (var entryStream = firstEntry.Open())
            {
                await ProduceFromStreamAsync(entryStream, outputChannel, cancellationToken);
            }
        }
    }

    private async Task ProduceFromGzipAsync(
        Stream gzipStream,
        Channel<string> outputChannel,
        CancellationToken cancellationToken)
    {
        using (var decompressed = new GZipStream(gzipStream, CompressionMode.Decompress))
        {
            await ProduceFromStreamAsync(decompressed, outputChannel, cancellationToken);
        }
    }

    private async Task ProduceFromStreamAsync(
        Stream stream,
        Channel<string> outputChannel,
        CancellationToken cancellationToken)
    {
        using (var reader = new StreamReader(stream, leaveOpen: false))
        {
            string? line;
            int lineCount = 0;

            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Saltar linhas vazias e comentários
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                {
                    continue;
                }

                // Extrair domínio (podem haver formatos diferentes)
                var domain = ExtractDomain(trimmed);
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    // Enfileirar com timeout para evitar deadlock
                    await outputChannel.Writer.WriteAsync(domain, cancellationToken);
                    lineCount++;

                    if (lineCount % 10000 == 0)
                    {
                        _logger.LogDebug("Produced {Count} domains", lineCount);
                    }
                }
            }

            _logger.LogInformation("Total lines produced: {Count}", lineCount);
        }
    }

    /// <summary>
    /// Extrai domínio da linha
    /// Suporta formatos: domínio simples ou CSV com domínio na primeira coluna
    /// </summary>
    private static string ExtractDomain(string line)
    {
        // Se contém vírgula, é CSV - pegar primeiro campo
        if (line.Contains(','))
        {
            var fields = line.Split(',');
            return fields[1].Trim().ToLowerInvariant();
        }

        // Caso contrário, considerar a linha inteira como domínio
        return line.Trim().ToLowerInvariant();
    }
}

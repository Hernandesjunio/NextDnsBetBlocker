namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.IO.Compression;
using System.Text.RegularExpressions;
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
    private static readonly Regex LocalPatternRegex = new(@"local=/([^/]+)/", RegexOptions.Compiled);

    public ListImportProducer(ILogger<ListImportProducer> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Lê dados de múltiplas origens em streaming e escreve no channel
    /// Suporta URLs HTTP/HTTPS e arquivos locais
    /// Processa em série com retry automático
    /// Descompacta automaticamente se for ZIP ou GZIP
    /// </summary>
    public async Task ProduceAsync(
        Channel<string> outputChannel,
        ListImportConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting production for {ListName} from {SourceCount} sources",
                config.ListName,
                config.SourceUrl.Length);

            // Processar cada fonte em série
            foreach (var sourceUrl in config.SourceUrl)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ProduceFromSourceAsync(sourceUrl, config.ListName, outputChannel, cancellationToken);
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

    /// <summary>
    /// Processa uma única fonte com retry automático (3 tentativas)
    /// </summary>
    private async Task ProduceFromSourceAsync(
        string sourceUrl,
        string listName,
        Channel<string> outputChannel,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        int attempt = 0;

        while (attempt < maxRetries)
        {
            attempt++;

            try
            {
                _logger.LogInformation(
                    "Processing source {SourceUrl} for {ListName} (attempt {Attempt}/{MaxRetries})",
                    sourceUrl, listName, attempt, maxRetries);

                await using var stream = await GetSourceStreamAsync(sourceUrl, cancellationToken);
                if (stream == null)
                {
                    throw new InvalidOperationException($"Could not open source: {sourceUrl}");
                }

                // Determinar tipo de arquivo e processar
                if (sourceUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await ProduceFromZipAsync(stream, outputChannel, cancellationToken);
                }
                else if (sourceUrl.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase) ||
                         sourceUrl.EndsWith(".txt.gz", StringComparison.OrdinalIgnoreCase))
                {
                    await ProduceFromGzipAsync(stream, outputChannel, cancellationToken);
                }
                else
                {
                    await ProduceFromStreamAsync(stream, outputChannel, cancellationToken);
                }

                _logger.LogInformation(
                    "Successfully processed source {SourceUrl} for {ListName}",
                    sourceUrl, listName);
                return; // Sucesso, sair da tentativa
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt < maxRetries)
                {
                    var delayMs = (int)Math.Pow(2, attempt - 1) * 1000; // Exponential backoff: 1s, 2s, 4s
                    _logger.LogWarning(
                        ex,
                        "Failed to process source {SourceUrl} (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms",
                        sourceUrl, attempt, maxRetries, delayMs);

                    await Task.Delay(delayMs, cancellationToken);
                }
                else
                {
                    _logger.LogError(
                        ex,
                        "Failed to process source {SourceUrl} after {MaxRetries} attempts",
                        sourceUrl, maxRetries);
                    throw;
                }
            }
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
                        _logger.LogDebug("Produced {Count} domains from current source", lineCount);
                    }
                }
            }

            _logger.LogInformation("Source processing completed: {Count} items produced", lineCount);
        }
    }

    /// <summary>
    /// Extrai domínio da linha
    /// Suporta formatos: domínio simples, CSV com domínio na segunda coluna, ou local=/dominio/
    /// </summary>
    private static string ExtractDomain(string line)
    {
        // Se contém padrão local=/dominio/
        var localMatch = LocalPatternRegex.Match(line);
        if (localMatch.Success)
        {
            return localMatch.Groups[1].Value.Trim().ToLowerInvariant();
        }

        // Se contém vírgula, é CSV - pegar segundo campo
        if (line.Contains(','))
        {
            var fields = line.Split(',');
            if (fields.Length > 1)
            {
                return fields[1].Trim().ToLowerInvariant();
            }
        }

        // Caso contrário, considerar a linha inteira como domínio
        return line.Trim().ToLowerInvariant();
    }
}

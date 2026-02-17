namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Implementação de download via HTTP para domínios.
/// Suporta URLs HTTP/HTTPS com retry automático.
/// </summary>
public class HttpDownloadService : IDownloadService
{
    private readonly ILogger<HttpDownloadService> _logger;

    public HttpDownloadService(ILogger<HttpDownloadService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Baixar e fazer parse de domínios de múltiplas fontes.
    /// Suporta URLs HTTP/HTTPS. Merge automático de múltiplas fontes.
    /// </summary>
    public async Task<HashSet<string>> DownloadAndParseAsync(
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
            throw new InvalidOperationException("No domains downloaded from any source");
        }

        return allDomains;
    }

    /// <summary>
    /// Baixar e fazer parse de uma única fonte com retry automático.
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
}

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
    private static readonly System.Text.RegularExpressions.Regex CommentLineRegex = 
        new(@"^[#!;]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex LocalFormatRegex =
        new(@"local=/([^/]+)/", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex AdblockPlusRegex =
        new(@"\|\|(.+)\^$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex CnameFormatRegex =
        new(@"^(\*\.)?([a-zA-Z0-9.-]+)\s+CNAME\s+\.$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex DomainValidationRegex =
        new(@"^(\*\.)?(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)*[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

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
    /// Usa streaming para minimizar o consumo de memória.
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

                // Usar streaming para reduzir consumo de memória
                using var stream = await httpClient.GetStreamAsync(sourceUrl, cancellationToken);
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    var trimmed = line.Trim();

                    // Ignorar linhas vazias e comentários
                    if (string.IsNullOrEmpty(trimmed) || CommentLineRegex.IsMatch(trimmed))
                        continue;

                    string domain;

                    // Suportar formato: "local=/domínio/"
                    if (trimmed.StartsWith("local=/"))
                    {
                        var match = LocalFormatRegex.Match(trimmed);
                        if (!match.Success)
                            continue;
                        domain = match.Groups[1].Value;
                    }
                    // Suportar formato Adblock Plus: "||domínio^"
                    else if (trimmed.StartsWith("||") && trimmed.EndsWith("^"))
                    {
                        var match = AdblockPlusRegex.Match(trimmed);
                        if (!match.Success)
                            continue;
                        domain = match.Groups[1].Value;
                    }
                    // Suportar formato DNS CNAME: "1xbet.ac CNAME ." ou "*.1xbet.ac CNAME ."
                    else if (trimmed.Contains(" CNAME "))
                    {
                        var match = CnameFormatRegex.Match(trimmed);
                        if (!match.Success)
                            continue;
                        domain = match.Groups[2].Value;
                    }
                    // Suportar formatos: "domínio" ou "rank,domínio" (Tranco)
                    else
                    {
                        domain = trimmed.Contains(',')
                            ? trimmed.Split(',')[1].Trim()
                            : trimmed;
                    }

                    if (!string.IsNullOrWhiteSpace(domain) && IsValidDomain(domain))
                    {
                        domains.Add(domain.ToLowerInvariant());
                    }

                    // Linha processada e descartada automaticamente (garbage collection)
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

    /// <summary>
    /// Valida se é um domínio válido conforme RFC 1123, com suporte opcional para wildcard.
    /// Aceita: exemplo.com, sub.exemplo.com, *.exemplo.com, *.sub.exemplo.com
    /// </summary>
    private static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253)
            return false;

        return DomainValidationRegex.IsMatch(domain);
    }
}

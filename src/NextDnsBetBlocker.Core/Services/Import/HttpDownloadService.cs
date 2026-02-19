namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Implementação de download via HTTP para domínios.
/// Suporta URLs HTTP/HTTPS com retry automático.
/// Usa IHttpClientFactory para reuso de conexões e connection pooling.
/// </summary>
public class HttpDownloadService : IDownloadService
{
    private readonly ILogger<HttpDownloadService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
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

    public HttpDownloadService(
        ILogger<HttpDownloadService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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
                // Usar factory para obter HttpClient com connection pooling automático
                using var httpClient = _httpClientFactory.CreateClient("HttpDownloadService");

                // Usar streaming para reduzir consumo de memória
                using var stream = await httpClient.GetStreamAsync(sourceUrl, cancellationToken);
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    // Usar Span para evitar alocações intermediárias
                    var lineSpan = line.AsSpan().Trim();

                    // Ignorar linhas vazias e comentários
                    if (lineSpan.IsEmpty || CommentLineRegex.IsMatch(lineSpan.ToString()))
                        continue;

                    // Extrair domínio como Span (sem alocação)
                    if (!TryExtractDomain(lineSpan, out var domainSpan))
                        continue;

                    // Validar domínio usando Span (minimal alocação apenas se necessário)
                    if (!IsValidDomainSpan(domainSpan))
                        continue;

                    // Converter para string apenas uma vez, já lowercase
                    var domainStr = domainSpan.ToString().ToLowerInvariant();
                    domains.Add(domainStr);

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
    /// Extrai o domínio da linha usando Span sem alocação intermediária.
    /// Suporta formatos: local=/domínio/, ||domínio^, domínio CNAME ., rank,domínio, domínio
    /// </summary>
    private static bool TryExtractDomain(ReadOnlySpan<char> line, out ReadOnlySpan<char> domain)
    {
        // Suportar formato: "local=/domínio/"
        if (line.StartsWith("local=/"))
        {
            var startIdx = "local=/".Length;
            var remaining = line[startIdx..];
            var endIdx = remaining.IndexOf('/');
            if (endIdx > 0)
            {
                domain = remaining[..endIdx];
                return true;
            }
        }
        // Suportar formato Adblock Plus: "||domínio^"
        else if (line.StartsWith("||") && line.EndsWith("^"))
        {
            domain = line[2..^1];
            return true;
        }
        // Suportar formato DNS CNAME: "1xbet.ac CNAME ." ou "*.1xbet.ac CNAME ."
        else if (line.Contains(" CNAME ", StringComparison.Ordinal))
        {
            var cnameSpan = " CNAME ";
            var cnameIdx = line.IndexOf(cnameSpan, StringComparison.Ordinal);
            if (cnameIdx > 0)
            {
                var domainPart = line[..cnameIdx];
                // Remove wildcard se existir
                if (domainPart.StartsWith("*."))
                {
                    domain = domainPart[2..];
                }
                else
                {
                    domain = domainPart;
                }
                return true;
            }
        }
        // Suportar formato Tranco: "rank,domínio"
        else if (line.Contains(','))
        {
            var commaIdx = line.IndexOf(',');
            if (commaIdx > 0 && commaIdx < line.Length - 1)
            {
                domain = line[(commaIdx + 1)..].Trim();
                return true;
            }
        }

        // Suportar formato simples: "domínio"
        domain = line;
        return true;
    }

    /// <summary>
    /// Valida se é um domínio válido conforme RFC 1123 usando Span.
    /// Conversão para string apenas se necessário (validação básica passa).
    /// </summary>
    private static bool IsValidDomainSpan(ReadOnlySpan<char> domain)
    {
        // Validações básicas em Span (sem alocação)
        if (domain.IsEmpty || domain.Length > 253)
            return false;

        // Se passou na validação básica, fazer a validação com regex (precisa de string)
        var domainStr = domain.ToString();
        return !string.IsNullOrWhiteSpace(domainStr) && DomainValidationRegex.IsMatch(domainStr);
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

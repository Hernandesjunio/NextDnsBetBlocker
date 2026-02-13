namespace NextDnsBetBlocker.Core.Services;

using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

public class GamblingSuspectAnalyzer : IGamblingSuspectAnalyzer
{
    private readonly ILogger<GamblingSuspectAnalyzer> _logger;
    private readonly HttpClient _httpClient;

    public GamblingSuspectAnalyzer(ILogger<GamblingSuspectAnalyzer> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public async Task<AnalysisResult> AnalyzeDomainAsync(string domain)
    {
        var result = new AnalysisResult
        {
            Domain = domain,
            AnalyzedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Starting gambling analysis for domain: {Domain}", domain);

            // Step 1: Domain structure analysis
            var domainScore = GamblingKeywordDetector.AnalyzeDomainStructure(domain);
            if (domainScore > 0)
            {
                result.Indicators.Add(new AnalysisIndicator
                {
                    Category = "Domain",
                    Indicator = $"suspicious-domain-pattern",
                    Score = domainScore,
                    Description = $"Domain name contains suspicious gambling patterns"
                });
            }

            // Step 2: WHOIS Analysis (Domain Age)
            var whoisScore = await AnalyzeWhoisAsync(domain);
            result.Indicators.AddRange(whoisScore.Indicators);

            // Step 3: SSL Certificate Analysis
            var sslScore = await AnalyzeSslAsync(domain);
            result.Indicators.AddRange(sslScore.Indicators);

            // Step 4: DNS Analysis
            var dnsScore = await AnalyzeDnsAsync(domain);
            result.Indicators.AddRange(dnsScore.Indicators);

            // Step 5: Content Analysis (HTML)
            var contentScore = await AnalyzeContentAsync(domain);
            result.Indicators.AddRange(contentScore.Indicators);

            // Calculate final score
            result.ConfidenceScore = CalculateConfidenceScore(result.Indicators);
            result.IsGambling = result.ConfidenceScore >= 70;
            result.Reason = GenerateReason(result.Indicators);

            _logger.LogInformation(
                "Analysis completed for {Domain}: Score={Score}, IsGambling={IsGambling}",
                domain, result.ConfidenceScore, result.IsGambling);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing domain: {Domain}", domain);
            result.ConfidenceScore = 0;
            result.IsGambling = false;
            result.Reason = $"Analysis error: {ex.Message}";
            return result;
        }
    }

    private async Task<AnalysisResult> AnalyzeWhoisAsync(string domain)
    {
        var result = new AnalysisResult { Domain = domain };

        try
        {
            _logger.LogDebug("Analyzing WHOIS data for: {Domain}", domain);

            // Try to resolve domain
            var addresses = await Dns.GetHostAddressesAsync(domain);
            if (addresses.Length == 0)
            {
                result.Indicators.Add(new AnalysisIndicator
                {
                    Category = "WHOIS",
                    Indicator = "domain-not-resolving",
                    Score = 20,
                    Description = "Domain does not resolve - potentially suspicious"
                });
                return result;
            }

            // Very simple heuristic: domains less than 30 days old are suspicious when combined with gambling indicators
            // In production, you'd use actual WHOIS API (like whoisxmlapi.com)
            result.Indicators.Add(new AnalysisIndicator
            {
                Category = "WHOIS",
                Indicator = "whois-check-attempted",
                Score = 0,
                Description = "Domain resolves - basic validation passed"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WHOIS analysis failed for: {Domain}", domain);
            result.Indicators.Add(new AnalysisIndicator
            {
                Category = "WHOIS",
                Indicator = "whois-check-failed",
                Score = 10,
                Description = "Could not perform WHOIS check"
            });
        }

        return result;
    }

    private async Task<AnalysisResult> AnalyzeSslAsync(string domain)
    {
        var result = new AnalysisResult { Domain = domain };

        try
        {
            _logger.LogDebug("Analyzing SSL certificate for: {Domain}", domain);

            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
            {
                if (cert == null) return false;

                var x509 = new X509Certificate2(cert);
                var issuer = x509.Issuer;

                // Check for self-signed certificates (red flag)
                if (issuer.Contains("self") || issuer.Equals($"CN={domain}", StringComparison.OrdinalIgnoreCase))
                {
                    result.Indicators.Add(new AnalysisIndicator
                    {
                        Category = "SSL",
                        Indicator = "self-signed-certificate",
                        Score = 25,
                        Description = "Self-signed SSL certificate detected"
                    });
                }

                // Check certificate expiry
                if (x509.NotAfter < DateTime.UtcNow.AddDays(7))
                {
                    result.Indicators.Add(new AnalysisIndicator
                    {
                        Category = "SSL",
                        Indicator = "certificate-expiring-soon",
                        Score = 15,
                        Description = "SSL certificate expiring within 7 days"
                    });
                }

                // Valid certificate from trusted issuer
                if (result.Indicators.Count == 0)
                {
                    result.Indicators.Add(new AnalysisIndicator
                    {
                        Category = "SSL",
                        Indicator = "valid-ssl-certificate",
                        Score = -10,
                        Description = "Valid SSL certificate from trusted issuer"
                    });
                }

                return true;
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var request = new HttpRequestMessage(HttpMethod.Head, $"https://{domain}");
            await client.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSL analysis failed for: {Domain}", domain);
            result.Indicators.Add(new AnalysisIndicator
            {
                Category = "SSL",
                Indicator = "ssl-check-failed",
                Score = 20,
                Description = "Could not validate SSL certificate"
            });
        }

        return result;
    }

    private async Task<AnalysisResult> AnalyzeDnsAsync(string domain)
    {
        var result = new AnalysisResult { Domain = domain };

        try
        {
            _logger.LogDebug("Analyzing DNS records for: {Domain}", domain);

            var addresses = await Dns.GetHostAddressesAsync(domain);

            // Check for suspicious IP patterns (datacenter IPs, known VPN providers, etc)
            // This is simplified - in production use IP reputation APIs
            if (addresses.Any(ip => ip.ToString().StartsWith("10.") || ip.ToString().StartsWith("192.168.")))
            {
                result.Indicators.Add(new AnalysisIndicator
                {
                    Category = "DNS",
                    Indicator = "private-ip-address",
                    Score = 15,
                    Description = "Domain resolves to private IP address"
                });
            }

            if (addresses.Length > 3)
            {
                result.Indicators.Add(new AnalysisIndicator
                {
                    Category = "DNS",
                    Indicator = "multiple-dns-records",
                    Score = 10,
                    Description = $"Domain has {addresses.Length} DNS records (potential load balancing)"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS analysis failed for: {Domain}", domain);
            result.Indicators.Add(new AnalysisIndicator
            {
                Category = "DNS",
                Indicator = "dns-check-failed",
                Score = 10,
                Description = "Could not resolve DNS records"
            });
        }

        return result;
    }

    private async Task<AnalysisResult> AnalyzeContentAsync(string domain)
    {
        var result = new AnalysisResult { Domain = domain };

        try
        {
            _logger.LogDebug("Analyzing HTML content for: {Domain}", domain);

            var url = $"https://{domain}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                result.Indicators.Add(new AnalysisIndicator
                {
                    Category = "Content",
                    Indicator = "http-error",
                    Score = 10,
                    Description = $"HTTP {response.StatusCode} - may indicate suspicious content"
                });
                return result;
            }

            var htmlContent = await response.Content.ReadAsStringAsync();
            var (keywords, keywordScore) = GamblingKeywordDetector.DetectKeywords(htmlContent);

            if (keywords.Count > 0)
            {
                result.Indicators.Add(new AnalysisIndicator
                {
                    Category = "Content",
                    Indicator = $"gambling-keywords:{keywords.Count}",
                    Score = keywordScore,
                    Description = $"Found {keywords.Count} gambling-related keywords: {string.Join(", ", keywords.Take(5))}"
                });
            }

            // Check page title and meta for gambling indicators
            if (htmlContent.Contains("casino", StringComparison.OrdinalIgnoreCase) ||
                htmlContent.Contains("aposta", StringComparison.OrdinalIgnoreCase) ||
                htmlContent.Contains("jogo de azar", StringComparison.OrdinalIgnoreCase))
            {
                result.Indicators.Add(new AnalysisIndicator
                {
                    Category = "Content",
                    Indicator = "meta-tags-contain-gambling",
                    Score = 15,
                    Description = "Page meta tags contain gambling references"
                });
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not fetch content for: {Domain}", domain);
            result.Indicators.Add(new AnalysisIndicator
            {
                Category = "Content",
                Indicator = "content-fetch-failed",
                Score = 15,
                Description = $"Could not fetch page content: {ex.Message}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content analysis failed for: {Domain}", domain);
        }

        return result;
    }

    private static int CalculateConfidenceScore(List<AnalysisIndicator> indicators)
    {
        if (indicators.Count == 0)
            return 0;

        // Weighted scoring system
        var totalScore = indicators.Sum(i => i.Score);
        
        // Ensure score is between 0 and 100
        return Math.Max(0, Math.Min(100, totalScore));
    }

    private static string GenerateReason(List<AnalysisIndicator> indicators)
    {
        var topIndicators = indicators
            .OrderByDescending(i => i.Score)
            .Take(3)
            .Select(i => i.Description);

        return string.Join("; ", topIndicators);
    }
}

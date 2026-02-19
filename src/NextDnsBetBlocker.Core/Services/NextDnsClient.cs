namespace NextDnsBetBlocker.Core.Services;

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// NextDNS API Client
/// Configuração via IOptions<WorkerSettings>
/// API Key lido de appsettings.json ou User Secrets (NÃO hardcoded!)
/// OBSOLETE: Esta classe não está sendo utilizada na pipeline atual.
/// Use ILogsProducer para obtenção de logs.
/// </summary>
public class NextDnsClient : INextDnsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NextDnsClient> _logger;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public NextDnsClient(
        HttpClient httpClient,
        ILogger<NextDnsClient> logger,
        IOptions<WorkerSettings> options)  // ← IOptions<WorkerSettings> injetado
    {
        _httpClient = httpClient;
        _logger = logger;

        var settings = options.Value;
        _baseUrl = settings.NextDnsBaseUrl;
        _apiKey = settings.NextDnsApiKey;

        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException(
                "WorkerSettings:NextDnsApiKey is not configured. Please set it via User Secrets or appsettings.json");
        }

        _logger.LogInformation("NextDnsClient initialized with BaseUrl: {BaseUrl}", _baseUrl);
    }

    public async Task<NextDnsLogsResponse> GetLogsAsync(string profileId, string? cursor = null, int limit = 1000, DateTime? since = null)
    {
        return await GetLogsRangeAsync(profileId, cursor, limit, since, null);
    }

    /// <summary>
    /// Fetch logs with optional from/to range filtering
    /// </summary>
    public async Task<NextDnsLogsResponse> GetLogsRangeAsync(
        string profileId,
        string? cursor = null,
        int limit = 1000,
        DateTime? from = null,
        DateTime? to = null)
    {
        try
        {
            var url = $"{_baseUrl}/profiles/{profileId}/logs?limit={limit}&sort=asc&status=default";

            // Add from filter if provided
            // NextDNS API uses 'from' and 'to' parameters (Unix timestamps)
            if (from.HasValue)
            {
                var fromTimestamp = ((DateTimeOffset)from.Value.ToUniversalTime()).ToUnixTimeSeconds();
                url += $"&from={fromTimestamp}";
                _logger.LogDebug("Filtering logs from: {FromTimestamp} (Unix: {UnixTimestamp})", from.Value, fromTimestamp);
            }

            // Add to filter if provided
            if (to.HasValue)
            {
                var toTimestamp = ((DateTimeOffset)to.Value.ToUniversalTime()).ToUnixTimeSeconds();
                url += $"&to={toTimestamp}";
                _logger.LogDebug("Filtering logs to: {ToTimestamp} (Unix: {UnixTimestamp})", to.Value, toTimestamp);
            }

            if (!string.IsNullOrEmpty(cursor))
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", _apiKey);

            _logger.LogDebug("Requesting logs from URL: {Url}", url);
            var response = await RetryAsync(() => _httpClient.SendAsync(request));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch logs from NextDNS: {StatusCode}", response.StatusCode);
                throw new HttpRequestException($"NextDNS returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var result = JsonSerializer.Deserialize<NextDnsLogsResponse>(content, options);
            return result ?? new NextDnsLogsResponse();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching logs from NextDNS");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching logs from NextDNS");
            throw;
        }
    }

    public async Task BlockDomainAsync(string profileId, string domain, string reason = "")
    {
        try
        {
            var url = $"{_baseUrl}/profiles/{profileId}/rules";
            var requestBody = new
            {
                action = "block",
                domain = domain,
                description = reason
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("X-Api-Key", _apiKey);
            request.Content = content;

            var response = await RetryAsync(() => _httpClient.SendAsync(request));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to block domain {Domain}: {StatusCode}", domain, response.StatusCode);
                throw new HttpRequestException($"Failed to block domain: {response.StatusCode}");
            }

            _logger.LogInformation("Domain blocked: {Domain}", domain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error blocking domain {Domain}", domain);
            throw;
        }
    }

    public async Task UnblockDomainAsync(string profileId, string domain)
    {
        try
        {
            var url = $"{_baseUrl}/profiles/{profileId}/rules/{Uri.EscapeDataString(domain)}";
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("X-Api-Key", _apiKey);

            var response = await RetryAsync(() => _httpClient.SendAsync(request));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to unblock domain {Domain}: {StatusCode}", domain, response.StatusCode);
                throw new HttpRequestException($"Failed to unblock domain: {response.StatusCode}");
            }

            _logger.LogInformation("Domain unblocked: {Domain}", domain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unblocking domain {Domain}", domain);
            throw;
        }
    }

    public async Task<bool> AddToAllowlistAsync(string profileId, string domain)
    {
        try
        {
            var url = $"{_baseUrl}/profiles/{profileId}/allowlist";
            var requestBody = new { domain = domain };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("X-Api-Key", _apiKey);
            request.Content = content;

            var response = await RetryAsync(() => _httpClient.SendAsync(request));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to add domain to allowlist {Domain}: {StatusCode}", domain, response.StatusCode);
                return false;
            }

            _logger.LogInformation("Domain added to allowlist: {Domain}", domain);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding domain to allowlist {Domain}", domain);
            return false;
        }
    }

    public async Task<bool> AddToDenylistAsync(string profileId, DenylistBlockRequest request)
    {
        try
        {
            var url = $"{_baseUrl}/profiles/{profileId}/denylist";

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("X-Api-Key", _apiKey);
            httpRequest.Content = content;

            var response = await RetryAsync(() => _httpClient.SendAsync(httpRequest));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to add domain to denylist: {StatusCode}", response.StatusCode);
                return false;
            }

            _logger.LogInformation("Domain added to denylist");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding domain to denylist");
            return false;
        }
    }

    private async Task<HttpResponseMessage> RetryAsync(Func<Task<HttpResponseMessage>> operation, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (i < maxRetries - 1 && ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delayMs = (int)Math.Pow(2, i) * 1000;
                _logger.LogWarning("Rate limited. Retrying after {DelayMs}ms (attempt {Attempt}/{MaxRetries})", delayMs, i + 1, maxRetries);
                await Task.Delay(delayMs);
            }
        }

        return await operation();
    }
}

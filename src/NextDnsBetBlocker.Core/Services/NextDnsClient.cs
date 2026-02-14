namespace NextDnsBetBlocker.Core.Services;

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

public class NextDnsClient : INextDnsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NextDnsClient> _logger;
    private const string BaseUrl = "https://api.nextdns.io";
    private const string ApiKey = "9a0a16ee3b58ef4e894fe75b51caa354d9dec3e2";

    public NextDnsClient(HttpClient httpClient, ILogger<NextDnsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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
            var url = $"{BaseUrl}/profiles/{profileId}/logs?limit={limit}&sort=asc&status=default";

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
            request.Headers.Add("X-Api-Key", ApiKey);

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

            var result = JsonSerializer.Deserialize<NextDnsLogsResponse>(content, options)
                ?? new NextDnsLogsResponse();

            _logger.LogDebug("Received {LogCount} logs from NextDNS", result.Data.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching logs from NextDNS");
            throw;
        }
    }
    public async Task<bool> AddToDenylistAsync(string profileId, DenylistBlockRequest request)
    {
        var url = $"{BaseUrl}/profiles/{profileId}/denylist";
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(request, options);
        _logger.LogDebug("Sending denylist request: {Json}", json);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        httpRequest.Headers.Add("X-Api-Key", ApiKey);

        var response = await RetryAsync(() => _httpClient.SendAsync(httpRequest));

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to add domain {Domain} to denylist: {StatusCode} - {ErrorContent}", 
                request.Id, response.StatusCode, errorContent);
            return false;
        }

        _logger.LogInformation("Successfully added domain {Domain} to denylist", request.Id);
        return true;
    }

    public async Task<bool> AddToAllowlistAsync(string profileId, string domain)
    {
        var url = $"{BaseUrl}/profiles/{profileId}/allowlist";
        var request = new { id = domain };

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(request, options);
        _logger.LogDebug("Sending allowlist request: {Json}", json);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        httpRequest.Headers.Add("X-Api-Key", ApiKey);

        var response = await RetryAsync(() => _httpClient.SendAsync(httpRequest));

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to add domain {Domain} to allowlist: {StatusCode} - {ErrorContent}", 
                domain, response.StatusCode, errorContent);
            return false;
        }

        _logger.LogInformation("Successfully added domain {Domain} to allowlist", domain);
        return true;
    }

    private async Task<HttpResponseMessage> RetryAsync(
        Func<Task<HttpResponseMessage>> action,
        int maxRetries = 5)
    {
        int retryCount = 0;
        int delayMs = 1000;

        while (true)
        {
            try
            {
                var response = await action();

                // Retry on 429 (Too Many Requests) or 5xx
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (retryCount >= maxRetries)
                    {
                        return response;
                    }

                    _logger.LogWarning("Rate limited or server error ({StatusCode}), retrying in {DelayMs}ms", 
                        response.StatusCode, delayMs);

                    await Task.Delay(delayMs);
                    delayMs = Math.Min(delayMs * 2, 30000); // Exponential backoff, max 30s
                    retryCount++;
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (retryCount < maxRetries)
            {
                _logger.LogWarning(ex, "Request failed, retrying in {DelayMs}ms", delayMs);
                await Task.Delay(delayMs);
                delayMs = Math.Min(delayMs * 2, 30000);
                retryCount++;
            }
        }
    }
}

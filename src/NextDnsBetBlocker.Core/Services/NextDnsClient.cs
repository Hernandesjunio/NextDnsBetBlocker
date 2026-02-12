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

    public async Task<NextDnsLogsResponse> GetLogsAsync(string profileId, string? cursor = null, int limit = 1000)
    {
        var url = $"{BaseUrl}/profiles/{profileId}/logs?limit={limit}&sort=asc";
        if (!string.IsNullOrEmpty(cursor))
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Api-Key", ApiKey);

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

        return result;
    }

    public async Task<bool> AddToDenylistAsync(string profileId, DenylistBlockRequest request)
    {
        var url = $"{BaseUrl}/profiles/{profileId}/denylist";
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(request, options);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        httpRequest.Headers.Add("X-Api-Key", ApiKey);

        var response = await RetryAsync(() => _httpClient.SendAsync(httpRequest));

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to add domain {Domain} to denylist: {StatusCode}", 
                request.Id, response.StatusCode);
            return false;
        }

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

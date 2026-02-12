namespace NextDnsBetBlocker.Core.Services;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class AllowlistProvider : IAllowlistProvider
{
    private readonly string _allowlistPath;
    private readonly ILogger<AllowlistProvider> _logger;
    private HashSet<string> _allowlist = [];

    public AllowlistProvider(string allowlistPath, ILogger<AllowlistProvider> logger)
    {
        _allowlistPath = allowlistPath;
        _logger = logger;
    }

    public ISet<string> GetAllowlist()
    {
        return new HashSet<string>(_allowlist);
    }

    public async Task ReloadAsync()
    {
        try
        {
            if (!File.Exists(_allowlistPath))
            {
                _logger.LogWarning("Allowlist file not found at {Path}, creating empty list", _allowlistPath);
                _allowlist = [];
                return;
            }

            var lines = await File.ReadAllLinesAsync(_allowlistPath);
            var domains = lines
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                .Select(line => NormalizeDomain(line))
                .ToHashSet();

            _allowlist = domains;
            _logger.LogInformation("Loaded {Count} domains from allowlist", domains.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load allowlist from {Path}", _allowlistPath);
            throw;
        }
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().ToLowerInvariant().TrimEnd('.');
    }
}

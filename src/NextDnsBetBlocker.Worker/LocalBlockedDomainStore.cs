namespace NextDnsBetBlocker.Worker;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class LocalBlockedDomainStore : IBlockedDomainStore
{
    private readonly string _dataPath;
    private readonly ILogger<LocalBlockedDomainStore> _logger;

    public LocalBlockedDomainStore(ILogger<LocalBlockedDomainStore> logger)
    {
        _dataPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "blocked-domains.txt");
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
    }

    public Task<bool> IsBlockedAsync(string profileId, string domain)
    {
        try
        {
            if (!File.Exists(_dataPath))
                return Task.FromResult(false);

            var lines = File.ReadAllLines(_dataPath);
            var blocked = lines.Any(line => 
            {
                var parts = line.Split('|');
                return parts.Length >= 2 && parts[0] == profileId && parts[1] == domain;
            });

            return Task.FromResult(blocked);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if domain is blocked");
            return Task.FromResult(false);
        }
    }

    public Task MarkBlockedAsync(string profileId, string domain)
    {
        try
        {
            var entry = $"{profileId}|{domain}|{DateTime.UtcNow:O}";
            File.AppendAllText(_dataPath, entry + Environment.NewLine);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking domain as blocked");
            return Task.CompletedTask;
        }
    }

    public Task<IEnumerable<string>> GetAllBlockedDomainsAsync(string profileId)
    {
        try
        {
            if (!File.Exists(_dataPath))
                return Task.FromResult(Enumerable.Empty<string>());

            var lines = File.ReadAllLines(_dataPath);
            var domains = lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split('|'))
                .Where(parts => parts.Length >= 2 && parts[0] == profileId)
                .Select(parts => parts[1])
                .ToList();

            return Task.FromResult(domains.AsEnumerable());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving blocked domains");
            return Task.FromResult(Enumerable.Empty<string>());
        }
    }
}

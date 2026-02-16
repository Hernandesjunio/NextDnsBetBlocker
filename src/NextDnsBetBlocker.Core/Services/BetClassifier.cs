namespace NextDnsBetBlocker.Core.Services;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class BetClassifier : IBetClassifier
{
    private readonly IHageziProvider _hageziProvider;
    private readonly ILogger<BetClassifier> _logger;

    public BetClassifier(IHageziProvider hageziProvider, ILogger<BetClassifier> logger)
    {
        _hageziProvider = hageziProvider;
        _logger = logger;
    }

    [Obsolete("", error:true)]
    public bool IsBetDomain(string domain)
    {
        var normalizedDomain = NormalizeDomain(domain);
        var gamblingDomains = _hageziProvider.GetGamblingDomainsAsync().GetAwaiter().GetResult();

        // Check exact match
        if (gamblingDomains.Contains(normalizedDomain))
            return true;

        // Check parent domains (e.g., if "bet.example.com" is not found, check "example.com")
        var parts = normalizedDomain.Split('.');
        for (int i = 1; i < parts.Length; i++)
        {
            var parentDomain = string.Join(".", parts.Skip(i));
            if (gamblingDomains.Contains(parentDomain))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().ToLowerInvariant().TrimEnd('.');
    }
}

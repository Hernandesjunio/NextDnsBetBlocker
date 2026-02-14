namespace NextDnsBetBlocker.Core.Services;

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Consumidor que verifica domínios contra Tranco List
/// Se encontrado → allowlist automaticamente
/// Se não encontrado → passa para análise suspeita
/// </summary>
public class TrancoAllowlistConsumer : ITrancoAllowlistConsumer
{
    private readonly ITrancoAllowlistProvider _trancoProvider;
    private readonly INextDnsClient _nextDnsClient;
    private readonly ILogger<TrancoAllowlistConsumer> _logger;

    public TrancoAllowlistConsumer(
        ITrancoAllowlistProvider trancoProvider,
        INextDnsClient nextDnsClient,
        ILogger<TrancoAllowlistConsumer> logger)
    {
        _trancoProvider = trancoProvider;
        _nextDnsClient = nextDnsClient;
        _logger = logger;
    }

    public async Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("TrancoAllowlistConsumer started for profile {ProfileId}", profileId);

            var trancoList = await _trancoProvider.GetTrancoDomainsAsync();
            _logger.LogInformation("Loaded {Count} trusted domains from Tranco List", trancoList.Count);

            int processed = 0;
            int allowlisted = 0;
            int forwarded = 0;

            // Read all suspects from input channel
            await foreach (var suspectEntry in inputChannel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                processed++;

                var domain = suspectEntry.Domain.ToLowerInvariant();

                // Check if domain is in Tranco List (trusted)
                if (trancoList.Contains(domain))
                {
                    // Add to allowlist in NextDNS
                    try
                    {
                        var success = await _nextDnsClient.AddToAllowlistAsync(profileId, domain);
                        if (success)
                        {
                            allowlisted++;
                            _logger.LogInformation("✓ Added trusted domain {Domain} to allowlist (Tranco List)", domain);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to add domain {Domain} to allowlist", domain);
                    }

                    continue;
                }

                // Domain not in Tranco List - forward for analysis
                forwarded++;
                await outputChannel.Writer.WriteAsync(suspectEntry, cancellationToken);

                if (processed % 100 == 0)
                    _logger.LogDebug("Processed {Total} suspects, allowlisted: {Allowlisted}, forwarded: {Forwarded}", 
                        processed, allowlisted, forwarded);
            }

            _logger.LogInformation(
                "TrancoAllowlistConsumer completed: Processed={Processed}, Allowlisted={Allowlisted}, Forwarded={Forwarded}",
                processed, allowlisted, forwarded);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TrancoAllowlistConsumer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TrancoAllowlistConsumer failed");
            throw;
        }
        finally
        {
            // Signal completion to next consumer
            outputChannel.Writer.TryComplete();
        }
    }
}

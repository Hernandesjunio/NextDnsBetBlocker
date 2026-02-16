namespace NextDnsBetBlocker.Core.Services;

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Consumidor que filtra logs e identifica suspeitos
/// Verifica allowlist, HaGeZi gambling list, e BetClassifier
/// Apenas domínios suspeitos são encaminhados ao próximo canal
/// </summary>
public class ClassifierConsumer : IClassifierConsumer
{
    private readonly IBlockedDomainStore _blockedDomainStore;
    private readonly IHageziProvider _hageziProvider;    
    private readonly IBetClassifier _betClassifier;
    private readonly ILogger<ClassifierConsumer> _logger;

    public ClassifierConsumer(
        IBlockedDomainStore blockedDomainStore,
        IHageziProvider hageziProvider,        
        IBetClassifier betClassifier,
        ILogger<ClassifierConsumer> logger)
    {
        _blockedDomainStore = blockedDomainStore;
        _hageziProvider = hageziProvider;        
        _betClassifier = betClassifier;
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
            _logger.LogInformation("ClassifierConsumer started for profile {ProfileId}", profileId);
                        
            //var gamblingDomains = await _hageziProvider.GetGamblingDomainsAsync();

            int processed = 0;
            int allowlisted = 0;
            int alreadyBlocked = 0;
            int notGambling = 0;
            int suspects = 0;

            // Read all logs from input channel
            await foreach (var logEntry in inputChannel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                processed++;

                var domain = logEntry.Domain.ToLowerInvariant();

                // Check if already blocked
                if (await _blockedDomainStore.IsBlockedAsync(profileId, domain))
                {
                    alreadyBlocked++;
                    _logger.LogDebug("Domain {Domain} already blocked", domain);
                    continue;
                }

                /* 
                 * TODO refatorar para pegar do cache ou table storage                 
                 */

                //// Check if in HaGeZi gambling list (known gambling)
                //if (gamblingDomains.Contains(domain))
                //{
                //    // Already known gambling - block immediately
                //    await _blockedDomainStore.MarkBlockedAsync(profileId, domain);
                //    _logger.LogInformation("Blocked known gambling domain {Domain}", domain);
                //    continue;
                //}

                /* TODO refatorar para classificar sem buscar dados de fora*/
                // Check with BetClassifier
                //if (!_betClassifier.IsBetDomain(domain))
                //{
                //    notGambling++;
                //    _logger.LogDebug("Domain {Domain} is not classified as gambling", domain);
                //    continue;
                //}
                
                // Send with backpressure (waits if output channel buffer is full)
                await outputChannel.Writer.WriteAsync(logEntry, cancellationToken);

                if (processed % 100 == 0)
                    _logger.LogDebug("Classified {Processed} logs, suspects: {Suspects}", processed, suspects);
            }

            _logger.LogInformation(
                "ClassifierConsumer completed: Processed={Processed}, Allowlisted={Allowlisted}, AlreadyBlocked={AlreadyBlocked}, NotGambling={NotGambling}, Suspects={Suspects}",
                processed, allowlisted, alreadyBlocked, notGambling, suspects);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ClassifierConsumer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClassifierConsumer failed");
            throw;
        }
        finally
        {
            // Signal completion to next consumer
            outputChannel.Writer.TryComplete();
        }
    }
}

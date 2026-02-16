namespace NextDnsBetBlocker.Core.Services;

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Consumidor que filtra logs e identifica suspeitos
/// Verifica:
/// - Domínios já bloqueados (IBlockedDomainStore)
/// - Domínios conhecidos gambling (IHageziGamblingStore - Table Storage)
/// - Classificação (IBetClassifier)
/// Apenas domínios suspeitos são encaminhados ao próximo canal
/// </summary>
public class ClassifierConsumer : IClassifierConsumer
{
    private readonly IBlockedDomainStore _blockedDomainStore;
    private readonly IHageziGamblingStore _hageziGamblingStore;
    private readonly IBetClassifier _betClassifier;
    private readonly ILogger<ClassifierConsumer> _logger;

    public ClassifierConsumer(
        IBlockedDomainStore blockedDomainStore,
        IHageziGamblingStore hageziGamblingStore,
        IBetClassifier betClassifier,
        ILogger<ClassifierConsumer> logger)
    {
        _blockedDomainStore = blockedDomainStore;
        _hageziGamblingStore = hageziGamblingStore;
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

            int processed = 0;
            int alreadyBlocked = 0;
            int knownGambling = 0;
            int notGambling = 0;
            int suspects = 0;

            // Read all logs from input channel
            await foreach (var logEntry in inputChannel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                processed++;

                var domain = logEntry.Domain.ToLowerInvariant();

                // Check 1: If already blocked
                if (await _blockedDomainStore.IsBlockedAsync(profileId, domain))
                {
                    alreadyBlocked++;
                    _logger.LogDebug("Domain {Domain} already blocked", domain);
                    continue;
                }

                // Check 2: If in HaGeZi gambling list (known gambling - Table Storage)
                if (await _hageziGamblingStore.IsGamblingDomainAsync(domain))
                {
                    knownGambling++;
                    // Block immediately (já é conhecido como gambling)
                    await _blockedDomainStore.MarkBlockedAsync(profileId, domain);
                    _logger.LogInformation("Blocked known gambling domain {Domain}", domain);
                    continue;
                }

                // Check 3: Classify with BetClassifier
                if (!_betClassifier.IsBetDomain(domain))
                {
                    notGambling++;
                    _logger.LogDebug("Domain {Domain} is not classified as gambling", domain);
                    continue;
                }

                // Domain is suspicious - forward to next consumer
                suspects++;
                await outputChannel.Writer.WriteAsync(logEntry, cancellationToken);

                if (processed % 100 == 0)
                    _logger.LogDebug("Processed {Processed} logs | Blocked: {Blocked} | Known Gambling: {Known} | Not Gambling: {NotGambling} | Suspects: {Suspects}",
                        processed, alreadyBlocked, knownGambling, notGambling, suspects);
            }

            _logger.LogInformation(
                "ClassifierConsumer completed: Processed={Processed}, AlreadyBlocked={AlreadyBlocked}, KnownGambling={KnownGambling}, NotGambling={NotGambling}, Suspects={Suspects}",
                processed, alreadyBlocked, knownGambling, notGambling, suspects);
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

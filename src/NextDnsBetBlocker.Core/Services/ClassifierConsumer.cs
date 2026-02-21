namespace NextDnsBetBlocker.Core.Services;

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Consumidor que filtra logs e identifica suspeitos
/// Verifica:
/// - Domínios já bloqueados (IBlockedDomainStore)
/// - Domínios conhecidos gambling (IHageziGamblingStore - Table Storage)
/// - Classificação (IBetClassifier)
/// Apenas domínios suspeitos são encaminhados ao próximo canal
/// PARALELIZADO: Suporta múltiplas threads configuráveis
/// </summary>
public class ClassifierConsumer : IClassifierConsumer
{
    private readonly IBlockedDomainStore _blockedDomainStore;
    private readonly IHageziGamblingStore _hageziGamblingStore;
    private readonly IBetClassifier _betClassifier;
    private readonly ILogger<ClassifierConsumer> _logger;
    private readonly IOptions<WorkerSettings> _workerSettings;

    private long _processed;
    private long _alreadyBlocked;
    private long _knownGambling;
    private long _notGambling;
    private long _suspects;

    public ClassifierConsumer(
        IBlockedDomainStore blockedDomainStore,
        IHageziGamblingStore hageziGamblingStore,
        IBetClassifier betClassifier,
        ILogger<ClassifierConsumer> logger,
        IOptions<WorkerSettings> workerSettings)
    {
        _blockedDomainStore = blockedDomainStore;
        _hageziGamblingStore = hageziGamblingStore;
        _betClassifier = betClassifier;
        _logger = logger;
        _workerSettings = workerSettings;
        _processed = 0;
        _alreadyBlocked = 0;
        _knownGambling = 0;
        _notGambling = 0;
        _suspects = 0;
    }

    public async Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            int consumerThreadCount = _workerSettings.Value.ClassifierConsumerThreadCount;

            _logger.LogInformation(
                "ClassifierConsumer started for profile {ProfileId} with {ThreadCount} parallel consumer threads",
                profileId,
                consumerThreadCount);

            // Reset counters
            _processed = 0;
            _alreadyBlocked = 0;
            _knownGambling = 0;
            _notGambling = 0;
            _suspects = 0;

            // Create concurrent consumer tasks
            var consumerTasks = new Task[consumerThreadCount];

            for (int i = 0; i < consumerThreadCount; i++)
            {
                consumerTasks[i] = ConsumeAndClassifyAsync(
                    inputChannel,
                    outputChannel,
                    profileId,
                    cancellationToken);
            }

            // Wait for all consumer threads to complete
            await Task.WhenAll(consumerTasks);

            _logger.LogInformation(
                "ClassifierConsumer completed: Processed={Processed}, AlreadyBlocked={AlreadyBlocked}, KnownGambling={KnownGambling}, NotGambling={NotGambling}, Suspects={Suspects}",
                _processed, _alreadyBlocked, _knownGambling, _notGambling, _suspects);
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

    private async Task ConsumeAndClassifyAsync(
        Channel<SuspectDomainEntry> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        // Read all logs from input channel (multiple threads can read simultaneously)
        await foreach (var logEntry in inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var domain = logEntry.Domain.ToLowerInvariant();

                // Check 1: If already blocked
                if (await _blockedDomainStore.IsBlockedAsync(profileId, domain))
                {
                    Interlocked.Increment(ref _alreadyBlocked);
                    _logger.LogDebug("Domain {Domain} already blocked", domain);
                    Interlocked.Increment(ref _processed);
                    continue;
                }

                // Check 2: If in HaGeZi gambling list (known gambling - Table Storage)
                if (await _hageziGamblingStore.IsGamblingDomainAsync(domain, cancellationToken))
                {
                    Interlocked.Increment(ref _knownGambling);
                    // Block immediately (já é conhecido como gambling)
                    await _blockedDomainStore.MarkBlockedAsync(profileId, domain);
                    _logger.LogInformation("Blocked known gambling domain {Domain}", domain);
                    Interlocked.Increment(ref _processed);
                    continue;
                }

                // Domain is suspicious - forward to next consumer
                await outputChannel.Writer.WriteAsync(logEntry, cancellationToken);

                Interlocked.Increment(ref _suspects);
                Interlocked.Increment(ref _processed);

                if (_processed % 100 == 0)
                    _logger.LogDebug("Processed {Processed} logs | Blocked: {Blocked} | Known Gambling: {Known} | Suspects: {Suspects}",
                        _processed, _alreadyBlocked, _knownGambling, _suspects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error classifying domain");
                // Continue with next entry instead of crashing
                Interlocked.Increment(ref _processed);
            }
        }
    }
}

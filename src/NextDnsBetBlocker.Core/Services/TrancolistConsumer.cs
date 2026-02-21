namespace NextDnsBetBlocker.Core.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Threading.Channels;

/// <summary>
/// Consumidor que verifica domínios contra Tranco List
/// REFATORADO: Usa Table Storage queries ao invés de HashSet em memória
/// Se encontrado → allowlist automaticamente
/// Se não encontrado → passa para análise suspeita
/// PARALELIZADO: Suporta múltiplas threads configuráveis
/// </summary>
public class TrancoAllowlistConsumer : ITrancoAllowlistConsumer
{
    private readonly IListTableProvider _tableProvider;
    private readonly INextDnsClient _nextDnsClient;
    private readonly ILogger<TrancoAllowlistConsumer> _logger;
    private readonly IOptions<WorkerSettings> _workerSettings;

    private long _processed;
    private long _allowlisted;
    private long _suspect;

    private const string TrancoTableName = "TrancoList";

    public TrancoAllowlistConsumer(
        IListTableProvider tableProvider,
        INextDnsClient nextDnsClient,
        ILogger<TrancoAllowlistConsumer> logger,
        IOptions<WorkerSettings> workerSettings)
    {
        _tableProvider = tableProvider;
        _nextDnsClient = nextDnsClient;
        _logger = logger;
        _workerSettings = workerSettings;
        _processed = 0;
        _allowlisted = 0;
        _suspect = 0;
    }

    public async Task StartAsync(
        Channel<LogEntryData> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            int consumerThreadCount = _workerSettings.Value.TrancoAllowlistConsumerThreadCount;

            _logger.LogInformation(
                "TrancoAllowlistConsumer started for profile {ProfileId} with {ThreadCount} parallel consumer threads",
                profileId,
                consumerThreadCount);

            // Reset counters
            _processed = 0;
            _allowlisted = 0;
            _suspect = 0;

            // Create concurrent consumer tasks
            var consumerTasks = new Task[consumerThreadCount];

            for (int i = 0; i < consumerThreadCount; i++)
            {
                consumerTasks[i] = ConsumeAndProcessAsync(
                    inputChannel,
                    outputChannel,
                    profileId,
                    cancellationToken);
            }

            // Wait for all consumer threads to complete
            await Task.WhenAll(consumerTasks);

            _logger.LogInformation(
                "TrancoAllowlistConsumer completed: Processed={Processed}, Allowlisted={Allowlisted}, Suspect={Suspect}",
                _processed, _allowlisted, _suspect);
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

    private async Task ConsumeAndProcessAsync(
        Channel<LogEntryData> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        // Read all entries from input channel (multiple threads can read simultaneously)
        await foreach (var logEntry in inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var domain = logEntry.Domain.ToLowerInvariant();

                // Check if domain exists in Tranco List (Table Storage query)
                // Eficiente: point query + cache 5 minutos
                var exists = await _tableProvider.DomainExistsAsync(
                    TrancoTableName,
                    domain,
                    cancellationToken);

                if (exists)
                {
                    // Add to allowlist in NextDNS
                    try
                    {
                        var success = await _nextDnsClient.AddToAllowlistAsync(profileId, domain);
                        if (success)
                        {
                            Interlocked.Increment(ref _allowlisted);
                            _logger.LogInformation("✓ Added trusted domain {Domain} to allowlist (Tranco List)", domain);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to add domain {Domain} to allowlist", domain);
                    }

                    Interlocked.Increment(ref _processed);
                    continue;
                }

                // Domain not in Tranco List - forward for analysis
                var suspectEntry = new SuspectDomainEntry
                {
                    Domain = domain,
                    FirstSeen = logEntry.Timestamp,
                    ProfileId = profileId,
                    ClassificationScore = 0 // Will be set by analyzer
                };

                await outputChannel.Writer.WriteAsync(suspectEntry, cancellationToken);

                Interlocked.Increment(ref _suspect);
                Interlocked.Increment(ref _processed);

                if (_processed % 100 == 0)
                    _logger.LogDebug("Processed {Total} domains, allowlisted: {Allowlisted}, suspect: {Suspect}",
                        _processed, _allowlisted, _suspect);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing domain from Tranco check");
                // Continue with next entry instead of crashing
                Interlocked.Increment(ref _processed);
            }
        }
    }
}


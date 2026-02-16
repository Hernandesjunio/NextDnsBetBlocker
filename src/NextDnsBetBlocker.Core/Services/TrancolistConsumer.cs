namespace NextDnsBetBlocker.Core.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Threading.Channels;

/// <summary>
/// Consumidor que verifica domínios contra Tranco List
/// REFATORADO: Usa Table Storage queries ao invés de HashSet em memória
/// Se encontrado → allowlist automaticamente
/// Se não encontrado → passa para análise suspeita
/// </summary>
public class TrancoAllowlistConsumer : ITrancoAllowlistConsumer
{
    private readonly IListTableProvider _tableProvider;
    private readonly INextDnsClient _nextDnsClient;
    private readonly ILogger<TrancoAllowlistConsumer> _logger;

    private const string TrancoTableName = "TrancoList";

    public TrancoAllowlistConsumer(
        IListTableProvider tableProvider,
        INextDnsClient nextDnsClient,
        ILogger<TrancoAllowlistConsumer> logger)
    {
        _tableProvider = tableProvider;
        _nextDnsClient = nextDnsClient;
        _logger = logger;
    }

    public async Task StartAsync(
        Channel<LogEntryData> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("TrancoAllowlistConsumer started for profile {ProfileId}", profileId);

            int processed = 0;
            int allowlisted = 0;
            int suspect = 0;

            // Read all suspects from input channel
            await foreach (var logEntry in inputChannel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                processed++;

                var domain = logEntry.Domain.ToLowerInvariant();
               /*TODO fazer validação dos domínios de forma paralela suportando maxdegree or parallelism*/
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
                suspect++;

                var suspectEntry = new SuspectDomainEntry
                {
                    Domain = domain,
                    FirstSeen = logEntry.Timestamp,
                    ProfileId = profileId,
                    ClassificationScore = 0 // Will be set by analyzer
                };

                await outputChannel.Writer.WriteAsync(suspectEntry, cancellationToken);

                if (processed % 100 == 0)
                    _logger.LogDebug("Processed {Total} domains, allowlisted: {Allowlisted}, suspect: {Suspect}", 
                        processed, allowlisted, suspect);
            }

            _logger.LogInformation(
                "TrancoAllowlistConsumer completed: Processed={Processed}, Allowlisted={Allowlisted}, Suspect={Suspect}",
                processed, allowlisted, suspect);
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


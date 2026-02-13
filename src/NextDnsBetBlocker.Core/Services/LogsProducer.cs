namespace NextDnsBetBlocker.Core.Services;

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Producer que puxa logs do NextDNS e envia para o canal
/// Executa continuamente com backpressure automático do canal
/// </summary>
public class LogsProducer : ILogsProducer
{
    private readonly INextDnsClient _nextDnsClient;
    private readonly ICheckpointStore _checkpointStore;
    private readonly ILogger<LogsProducer> _logger;

    public LogsProducer(
        INextDnsClient nextDnsClient,
        ICheckpointStore checkpointStore,
        ILogger<LogsProducer> logger)
    {
        _nextDnsClient = nextDnsClient;
        _checkpointStore = checkpointStore;
        _logger = logger;
    }

    public async Task StartAsync(
        Channel<LogEntryData> channel,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("LogsProducer started for profile {ProfileId}", profileId);

            // Get last checkpoint
            var lastTimestamp = await _checkpointStore.GetLastTimestampAsync(profileId);
            _logger.LogInformation("Last checkpoint: {Timestamp}", lastTimestamp?.ToString("O") ?? "None");

            string? cursor = null;
            var newLastTimestamp = lastTimestamp ?? DateTime.MinValue;
            int totalProduced = 0;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Fetching logs for profile {ProfileId}, cursor: {Cursor}", profileId, cursor ?? "initial");

                var response = await _nextDnsClient.GetLogsRangeAsync(profileId, cursor, limit: 1000, from: lastTimestamp);

                if (response.Data.Count == 0)
                {
                    _logger.LogInformation("No new logs returned");
                    break;
                }

                _logger.LogDebug("Received {Count} logs from NextDNS", response.Data.Count);

                // Send logs to channel with backpressure
                foreach (var log in response.Data)
                {
                    // Track latest timestamp
                    if (log.Timestamp > newLastTimestamp)
                        newLastTimestamp = log.Timestamp;

                    var entry = new LogEntryData
                    {
                        Domain = log.Domain,
                        Timestamp = log.Timestamp,
                        ProfileId = profileId
                    };

                    // WriteAsync will wait if channel buffer is full (backpressure)
                    await channel.Writer.WriteAsync(entry, cancellationToken);
                    totalProduced++;

                    if (totalProduced % 100 == 0)
                        _logger.LogDebug("Produced {Total} logs so far", totalProduced);
                }

                cursor = response.Meta.Pagination.Cursor;

            } while (!string.IsNullOrEmpty(cursor) && !cancellationToken.IsCancellationRequested);

            // Update checkpoint
            if (newLastTimestamp > (lastTimestamp ?? DateTime.MinValue))
            {
                await _checkpointStore.UpdateLastTimestampAsync(profileId, newLastTimestamp);
                _logger.LogInformation("Updated checkpoint to {Timestamp}", newLastTimestamp.ToString("O"));
            }

            _logger.LogInformation("LogsProducer completed: produced {Total} logs", totalProduced);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LogsProducer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogsProducer failed");
            throw;
        }
        finally
        {
            // Signal completion to consumers
            channel.Writer.TryComplete();
        }
    }
}

namespace NextDnsBetBlocker.Core.Services;

using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class CheckpointStore : ICheckpointStore
{
    private readonly TableClient _tableClient;
    private readonly ILogger<CheckpointStore> _logger;
    private const string PartitionKey = "checkpoint";

    public CheckpointStore(TableServiceClient tableServiceClient, ILogger<CheckpointStore> logger)
    {
        var checkpointTableClient = tableServiceClient.GetTableClient("AgentState");
        checkpointTableClient.CreateIfNotExists();
        _tableClient = checkpointTableClient;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the checkpoint entity exists with default values.
    /// Should be called during application startup to initialize the checkpoint if it doesn't exist.
    /// </summary>
    public async Task EnsureCheckpointAsync(string profileId)
    {
        try
        {
            // Try to get existing checkpoint
            await _tableClient.GetEntityAsync<TableEntity>(PartitionKey, profileId);
            _logger.LogDebug("Checkpoint already exists for {ProfileId}", profileId);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Checkpoint doesn't exist, create with default timestamp
            try
            {
                var defaultTimestamp = DateTime.UtcNow.AddDays(-1);
                var entity = new TableEntity(PartitionKey, profileId)
                {
                    { "LastTimestamp", defaultTimestamp }
                };
                await _tableClient.AddEntityAsync(entity);
                _logger.LogInformation("Created checkpoint for {ProfileId} with default timestamp {Timestamp}", profileId, defaultTimestamp.ToString("O"));
            }
            catch (Exception createEx)
            {
                _logger.LogError(createEx, "Failed to create checkpoint for {ProfileId}", profileId);
                throw;
            }
        }
    }

    public async Task<DateTime?> GetLastTimestampAsync(string profileId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(PartitionKey, profileId);
            if (response?.Value == null)
            {
                _logger.LogInformation("No checkpoint found for {ProfileId}", profileId);
                return null;
            }

            var entity = response.Value;
            if (entity.TryGetValue("LastTimestamp", out var lastTimestamp))
            {
                // Ensure it's treated as UTC
                DateTime dt;
                if (lastTimestamp is DateTimeOffset dtoValue)
                {
                    dt = DateTime.SpecifyKind(dtoValue.Date, DateTimeKind.Utc);
                } else if (lastTimestamp is DateTime dtValue)
                {
                    dt = DateTime.SpecifyKind(dtValue, DateTimeKind.Utc);
                }
                else if (lastTimestamp is long unixTimestamp)
                {
                    dt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;
                }
                else
                {
                    _logger.LogWarning("Could not parse timestamp for {ProfileId}", profileId);
                    return null;
                }

                _logger.LogInformation("Retrieved checkpoint for {ProfileId}: {Timestamp} (Kind: {Kind})", profileId, dt, dt.Kind);
                return dt;
            }

            return null;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("No checkpoint found for {ProfileId}", profileId);
            return null;
        }
    }

    public async Task UpdateLastTimestampAsync(string profileId, DateTime timestamp)
    {
        try
        {
            // Ensure timestamp is UTC
            var utcTimestamp = timestamp.ToUniversalTime();

            var entity = new TableEntity(PartitionKey, profileId)
            {
                { "LastTimestamp", utcTimestamp },
                { "UpdatedAt", DateTime.UtcNow }
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            _logger.LogInformation("Updated checkpoint for {ProfileId} to {Timestamp} (Kind: {Kind})", profileId, utcTimestamp, utcTimestamp.Kind);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update checkpoint for {ProfileId}", profileId);
            throw;
        }
    }
}

namespace NextDnsBetBlocker.Core.Services;

using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

public class CheckpointStore : ICheckpointStore
{
    private readonly TableClient _tableClient;
    private readonly ILogger<CheckpointStore> _logger;
    private const string TableName = "AgentState";
    private const string PartitionKey = "checkpoint";

    public CheckpointStore(TableClient tableClient, ILogger<CheckpointStore> logger)
    {
        _tableClient = tableClient;
        _logger = logger;
    }

    public async Task<DateTime?> GetLastTimestampAsync(string profileId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(PartitionKey, profileId);
            if (response?.Value == null)
                return null;

            var entity = response.Value;
            if (entity.TryGetValue("LastTimestamp", out var lastTimestamp) && lastTimestamp is DateTime dt)
            {
                _logger.LogInformation("Retrieved checkpoint for {ProfileId}: {Timestamp}", profileId, dt);
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
            var entity = new TableEntity(PartitionKey, profileId)
            {
                { "LastTimestamp", timestamp }
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            _logger.LogInformation("Updated checkpoint for {ProfileId} to {Timestamp}", profileId, timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update checkpoint for {ProfileId}", profileId);
            throw;
        }
    }
}

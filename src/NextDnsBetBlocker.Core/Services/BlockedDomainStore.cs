namespace NextDnsBetBlocker.Core.Services;

using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// OBSOLETE: Esta classe não está sendo utilizada na pipeline atual.
/// Armazenamento de domínios bloqueados não é requerido pela pipeline ativa.
/// </summary>
public class BlockedDomainStore : IBlockedDomainStore
{
    private readonly TableClient _tableClient;
    private readonly ILogger<BlockedDomainStore> _logger;
    
    public BlockedDomainStore(TableServiceClient tableServiceClient, ILogger<BlockedDomainStore> logger)
    {
        _tableClient = tableServiceClient.GetTableClient("BlockedDomains");
        _logger = logger;
    }

    public async Task<bool> IsBlockedAsync(string profileId, string domain)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(profileId, domain);
            return response?.Value != null;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task MarkBlockedAsync(string profileId, string domain)
    {
        try
        {
            var entity = new TableEntity(profileId, domain)
            {
                { "BlockedAt", DateTime.UtcNow }
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            _logger.LogDebug("Marked domain {Domain} as blocked in profile {ProfileId}", domain, profileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark domain {Domain} as blocked", domain);
            throw;
        }
    }

    public async Task<IEnumerable<string>> GetAllBlockedDomainsAsync(string profileId)
    {
        try
        {
            var domains = new List<string>();
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(e => e.PartitionKey == profileId))
            {
                domains.Add(entity.RowKey);
            }

            _logger.LogInformation("Retrieved {Count} blocked domains for profile {ProfileId}", domains.Count, profileId);
            return domains;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve blocked domains for profile {ProfileId}", profileId);
            throw;
        }
    }
}

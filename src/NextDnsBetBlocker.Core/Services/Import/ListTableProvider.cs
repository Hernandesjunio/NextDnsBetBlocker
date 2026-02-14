namespace NextDnsBetBlocker.Core.Services.Import;

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Provider genérico para queries eficientes no Table Storage
/// Implementa cache de curta duração para melhor performance
/// Suporta point queries e batch lookups
/// </summary>
public class ListTableProvider : IListTableProvider
{
    private readonly TableClient _tableClient;
    private readonly IMemoryCache _cache;
    private readonly IPartitionKeyStrategy _partitionKeyStrategy;
    private readonly ILogger<ListTableProvider> _logger;
    
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public ListTableProvider(
        TableClient tableClient,
        IMemoryCache cache,
        IPartitionKeyStrategy partitionKeyStrategy,
        ILogger<ListTableProvider> logger)
    {
        _tableClient = tableClient;
        _cache = cache;
        _partitionKeyStrategy = partitionKeyStrategy;
        _logger = logger;
    }

    public async Task<bool> DomainExistsAsync(
        string tableName,
        string domain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        domain = domain.ToLowerInvariant();
        
        // Tentar cache primeiro
        var cacheKey = GetCacheKey(tableName, domain);
        if (_cache.TryGetValue(cacheKey, out bool exists))
        {
            return exists;
        }

        try
        {
            // Query ponto exato (muito rápido)
            var partitionKey = _partitionKeyStrategy.GetPartitionKey(domain);
            
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                partitionKey,
                domain,
                cancellationToken: cancellationToken);

            var found = response.Value != null;
            
            // Cache por 5 minutos
            _cache.Set(cacheKey, found, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheDuration
            });

            return found;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Domínio não existe - cache mesmo assim
            _cache.Set(cacheKey, false, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheDuration
            });
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking domain existence in table {TableName}: {Domain}", tableName, domain);
            throw;
        }
    }

    public async Task<DomainListEntry?> GetDomainAsync(
        string tableName,
        string domain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        domain = domain.ToLowerInvariant();

        try
        {
            var partitionKey = _partitionKeyStrategy.GetPartitionKey(domain);
            
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                partitionKey,
                domain,
                cancellationToken: cancellationToken);

            if (response.Value == null)
                return null;

            var timestamp = response.Value.Timestamp.HasValue 
                ? response.Value.Timestamp.Value.UtcDateTime 
                : DateTime.UtcNow;

            return new DomainListEntry
            {
                PartitionKey = response.Value.PartitionKey,
                RowKey = response.Value.RowKey,
                Timestamp = timestamp,
                ETag = response.Value.ETag.ToString()
            };
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving domain from table {TableName}: {Domain}", tableName, domain);
            throw;
        }
    }

    public async Task<List<DomainListEntry>> GetByPartitionAsync(
        string tableName,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = _tableClient.QueryAsync<TableEntity>(
                e => e.PartitionKey == partitionKey,
                cancellationToken: cancellationToken);

            var entries = new List<DomainListEntry>();

            await foreach (var entity in query)
            {
                var timestamp = entity.Timestamp.HasValue 
                    ? entity.Timestamp.Value.UtcDateTime 
                    : DateTime.UtcNow;

                entries.Add(new DomainListEntry
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey,
                    Timestamp = timestamp,
                    ETag = entity.ETag.ToString()
                });
            }

            _logger.LogInformation(
                "Retrieved {Count} entries from partition {Partition} in table {TableName}",
                entries.Count, partitionKey, tableName);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving partition {Partition} from table {TableName}", partitionKey, tableName);
            throw;
        }
    }

    public async Task<long> CountAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check cache first
            var cacheKey = GetCacheKey(tableName, "COUNT");
            if (_cache.TryGetValue(cacheKey, out long cachedCount))
            {
                return cachedCount;
            }

            // Count all entities (sem filtro = mais lento, mas sem alternativa)
            var query = _tableClient.QueryAsync<TableEntity>(cancellationToken: cancellationToken);
            
            long count = 0;
            await foreach (var _ in query)
            {
                count++;
            }

            // Cache por 1 minuto (menos tempo para count)
            _cache.Set(cacheKey, count, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });

            _logger.LogInformation("Table {TableName} has {Count} entries", tableName, count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting entries in table {TableName}", tableName);
            throw;
        }
    }

    public async Task<Dictionary<string, bool>> DomainExistsBatchAsync(
        string tableName,
        List<string> domains,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, bool>();

        if (domains == null || domains.Count == 0)
            return result;

        // Agrupar domínios por partition key para otimizar queries
        var byPartition = domains
            .Select(d => d.ToLowerInvariant())
            .GroupBy(d => _partitionKeyStrategy.GetPartitionKey(d))
            .ToList();

        foreach (var partitionGroup in byPartition)
        {
            try
            {
                // Query todos da partição
                var rowKeys = partitionGroup.ToHashSet();
                var query = _tableClient.QueryAsync<TableEntity>(
                    e => e.PartitionKey == partitionGroup.Key,
                    cancellationToken: cancellationToken);

                var foundInPartition = new HashSet<string>();

                await foreach (var entity in query)
                {
                    foundInPartition.Add(entity.RowKey.ToLowerInvariant());
                }

                // Mapear resultado
                foreach (var domain in rowKeys)
                {
                    var exists = foundInPartition.Contains(domain);
                    result[domain] = exists;

                    // Cache cada um
                    var cacheKey = GetCacheKey(tableName, domain);
                    _cache.Set(cacheKey, exists, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _cacheDuration
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error batch checking domains in partition {Partition}", partitionGroup.Key);
                
                // Marcar todos dessa partição como "erro" (false por segurança)
                foreach (var domain in partitionGroup)
                {
                    result[domain] = false;
                }
            }
        }

        _logger.LogDebug("Batch check completed for {Count} domains", domains.Count);
        return result;
    }

    private static string GetCacheKey(string tableName, string domain)
    {
        return $"domain:{tableName}:{domain}";
    }
}

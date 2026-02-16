namespace NextDnsBetBlocker.Core.Services.Import;

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Collections.Concurrent;

/// <summary>
/// Repositório para acesso ao Table Storage
/// Implementa operações em batch com resiliência
/// Pool de TableClient por tabela para melhor performance
/// </summary>
public class ListTableStorageRepository : IListTableStorageRepository
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<ListTableStorageRepository> _logger;
    private readonly ConcurrentDictionary<string, TableClient> _tableClientCache;
    private const int MaxBatchSize = 100; // Limite do Table Storage

    public ListTableStorageRepository(
        string connectionString,
        ILogger<ListTableStorageRepository> logger)
    {
        _logger = logger;
        _tableServiceClient = new TableServiceClient(connectionString);
        _tableClientCache = new ConcurrentDictionary<string, TableClient>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Obter ou criar TableClient com pool em cache
    /// </summary>
    private TableClient GetOrCreateTableClient(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be empty", nameof(tableName));

        return _tableClientCache.GetOrAdd(tableName, 
            name => _tableServiceClient.GetTableClient(name));
    }

    public async Task<BatchOperationResult> UpsertBatchAsync(
        string tableName,
        List<DomainListEntry> entries,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required", nameof(tableName));

        var result = new BatchOperationResult
        {
            ItemCount = entries.Count,
            SuccessCount = 0,
            FailureCount = 0
        };

        if (entries.Count == 0)
        {
            return result;
        }

        try
        {
            // Validar limite do Table Storage
            if (entries.Count > MaxBatchSize)
            {
                _logger.LogDebug(
                    "Batch size {Count} exceeds max {Max}. Will split into {ChunkCount} batches.",
                    entries.Count,
                    MaxBatchSize,
                    (entries.Count / MaxBatchSize) + (entries.Count % MaxBatchSize > 0 ? 1 : 0));

                // Processar em chunks
                var chunks = entries
                    .Chunk(MaxBatchSize)
                    .ToList();

                foreach (var chunk in chunks)
                {
                    var chunkResult = await ProcessBatchChunkAsync(tableName, chunk.ToList(), cancellationToken);
                    result.SuccessCount += chunkResult.SuccessCount;
                    result.FailureCount += chunkResult.FailureCount;
                }

                return result;
            }

            // Processar batch único
            return await ProcessBatchChunkAsync(tableName, entries, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch upsert failed for table {TableName}", tableName);
            result.FailureCount = entries.Count;
            result.ErrorMessage = ex.Message;
            throw;
        }
    }

    public async Task<BatchOperationResult> DeleteBatchAsync(
        string tableName,
        List<DomainListEntry> entries,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required", nameof(tableName));

        var result = new BatchOperationResult
        {
            ItemCount = entries.Count,
            SuccessCount = 0,
            FailureCount = 0
        };

        if (entries.Count == 0)
        {
            return result;
        }

        try
        {
            // Validar limite do Table Storage
            if (entries.Count > MaxBatchSize)
            {
                var chunks = entries
                    .Chunk(MaxBatchSize)
                    .ToList();

                foreach (var chunk in chunks)
                {
                    var chunkResult = await ProcessDeleteChunkAsync(tableName, chunk.ToList(), cancellationToken);
                    result.SuccessCount += chunkResult.SuccessCount;
                    result.FailureCount += chunkResult.FailureCount;
                }

                return result;
            }

            // Processar delete único
            return await ProcessDeleteChunkAsync(tableName, entries, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch delete failed for table {TableName}", tableName);
            result.FailureCount = entries.Count;
            result.ErrorMessage = ex.Message;
            throw;
        }
    }

    public async Task<bool> DomainExistsAsync(
        string tableName,
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required", nameof(tableName));

        try
        {
            var tableClient = GetOrCreateTableClient(tableName);
            var response = await tableClient.GetEntityAsync<TableEntity>(
                partitionKey,
                rowKey,
                cancellationToken: cancellationToken);

            return response.Value != null;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking domain existence in table {TableName}: {PartitionKey}/{RowKey}", tableName, partitionKey, rowKey);
            throw;
        }
    }

    public async Task EnsureTableExistsAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required", nameof(tableName));

        try
        {
            var tableClient = GetOrCreateTableClient(tableName);
            await tableClient.CreateAsync(cancellationToken);
            _logger.LogInformation("Table {TableName} created or already exists", tableName);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 409)
        {
            // Tabela já existe
            _logger.LogDebug("Table {TableName} already exists", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure table {TableName} exists", tableName);
            throw;
        }
    }

    private async Task<BatchOperationResult> ProcessBatchChunkAsync(
        string tableName,
        List<DomainListEntry> entries,
        CancellationToken cancellationToken)
    {
        var result = new BatchOperationResult
        {
            ItemCount = entries.Count,
            SuccessCount = 0,
            FailureCount = 0
        };

        try
        {
            var tableClient = GetOrCreateTableClient(tableName);

            // Agrupar por partition key para batch transactions
            var byPartition = entries
                .GroupBy(e => e.PartitionKey)
                .ToList();

            foreach (var partitionGroup in byPartition)
            {
                result.PartitionsAffected.Add(partitionGroup.Key);

                // Criar batch transaction para a partição
                var batch = new List<TableTransactionAction>();

                foreach (var entry in partitionGroup)
                {
                    // Converter para TableEntity
                    var tableEntity = new TableEntity(entry.PartitionKey, entry.RowKey)
                    {
                        Timestamp = entry.Timestamp
                    };

                    // Usar UpdateReplace para upsert
                    batch.Add(new TableTransactionAction(
                        TableTransactionActionType.UpsertReplace,
                        tableEntity));
                }

                // Executar batch transaction
                try
                {
                    var response = await tableClient.SubmitTransactionAsync(batch, cancellationToken);
                    result.SuccessCount += batch.Count;
                    _logger.LogDebug(
                        "Batch transaction succeeded in table {TableName}: {Count} items in partition {Partition}",
                        tableName,
                        batch.Count,
                        partitionGroup.Key);
                }
                catch (Exception ex)
                {
                    result.FailureCount += batch.Count;
                    _logger.LogError(ex, "Batch transaction failed for table {TableName} partition {Partition}", tableName, partitionGroup.Key);
                    throw;
                }
            }

            result.ElapsedMilliseconds = 0; // TODO: Rastrear com Stopwatch no consumer
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process batch chunk for table {TableName}", tableName);
            throw;
        }
    }

    private async Task<BatchOperationResult> ProcessDeleteChunkAsync(
        string tableName,
        List<DomainListEntry> entries,
        CancellationToken cancellationToken)
    {
        var result = new BatchOperationResult
        {
            ItemCount = entries.Count,
            SuccessCount = 0,
            FailureCount = 0
        };

        try
        {
            var tableClient = GetOrCreateTableClient(tableName);

            // Agrupar por partition key
            var byPartition = entries
                .GroupBy(e => e.PartitionKey)
                .ToList();

            foreach (var partitionGroup in byPartition)
            {
                result.PartitionsAffected.Add(partitionGroup.Key);

                // Criar batch transaction para delete
                var batch = new List<TableTransactionAction>();

                foreach (var entry in partitionGroup)
                {
                    var tableEntity = new TableEntity(entry.PartitionKey, entry.RowKey)
                    {
                        ETag = ETag.All // Delete regardless of ETag
                    };

                    batch.Add(new TableTransactionAction(
                        TableTransactionActionType.Delete,
                        tableEntity));
                }

                try
                {
                    await tableClient.SubmitTransactionAsync(batch, cancellationToken);
                    result.SuccessCount += batch.Count;
                    _logger.LogDebug(
                        "Delete batch succeeded in table {TableName}: {Count} items from partition {Partition}",
                        tableName,
                        batch.Count,
                        partitionGroup.Key);
                }
                catch (Exception ex)
                {
                    result.FailureCount += batch.Count;
                    _logger.LogError(ex, "Delete batch failed for table {TableName} partition {Partition}", tableName, partitionGroup.Key);
                    throw;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process delete chunk for table {TableName}", tableName);
            throw;
        }
    }
}

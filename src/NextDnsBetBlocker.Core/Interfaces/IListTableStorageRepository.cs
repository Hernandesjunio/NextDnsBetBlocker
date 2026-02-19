namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Repositório genérico para listas de domínios em Table Storage
/// </summary>
public interface IListTableStorageRepository
{
    /// <summary>
    /// Insere ou atualiza múltiplos registros em batch
    /// </summary>
    Task<BatchOperationResult> UpsertBatchAsync(
        string tableName,
        List<DomainListEntry> entries,
        CancellationToken cancellationToken);

    /// <summary>
    /// Remove múltiplos registros em batch
    /// </summary>
    Task<BatchOperationResult> DeleteBatchAsync(
        string tableName,
        List<DomainListEntry> entries,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifica se domínio existe
    /// </summary>
    Task<bool> DomainExistsAsync(
        string tableName,
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cria tabela se não existir
    /// </summary>
    Task EnsureTableExistsAsync(
        string tableName,
        CancellationToken cancellationToken);
}

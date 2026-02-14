namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Provider genérico para leitura eficiente de listas de domínios no Table Storage
/// Suporta queries pontuais e em batch com cache de curta duração
/// </summary>
public interface IListTableProvider
{
    /// <summary>
    /// Verifica se domínio existe na tabela
    /// Query otimizado: ponto exato (partition + row key)
    /// Cache: 5 minutos
    /// </summary>
    Task<bool> DomainExistsAsync(
        string tableName,
        string domain,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recupera entidade completa do domínio
    /// </summary>
    Task<DomainListEntry?> GetDomainAsync(
        string tableName,
        string domain,
        CancellationToken cancellationToken);

    /// <summary>
    /// Busca todos os domínios de uma partição
    /// Para debug/admin/análise
    /// </summary>
    Task<List<DomainListEntry>> GetByPartitionAsync(
        string tableName,
        string partitionKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Conta total de registros na tabela
    /// </summary>
    Task<long> CountAsync(
        string tableName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifica múltiplos domínios em batch
    /// Mais eficiente que chamar DomainExistsAsync múltiplas vezes
    /// </summary>
    Task<Dictionary<string, bool>> DomainExistsBatchAsync(
        string tableName,
        List<string> domains,
        CancellationToken cancellationToken);
}

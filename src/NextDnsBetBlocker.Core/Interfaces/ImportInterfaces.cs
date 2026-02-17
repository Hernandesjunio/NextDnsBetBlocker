namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;
using System.Threading.Channels;

/// <summary>
/// Estratégia para gerar partition key baseado em domínio
/// Usado para sharding em Table Storage
/// </summary>
public interface IPartitionKeyStrategy
{
    /// <summary>
    /// Gera a partition key para um domínio
    /// Deve ser determinístico: mesmo domínio → sempre mesma partição
    /// </summary>
    string GetPartitionKey(string domain);

    /// <summary>
    /// Retorna o número de partições usadas
    /// </summary>
    int GetPartitionCount();
}

/// <summary>
/// Produtor de dados da origem (arquivo/URL/blob)
/// Responsável por ler e enfileirar dados
/// </summary>
public interface IListImportProducer
{
    /// <summary>
    /// Lê dados da origem e escreve no channel
    /// Processa em streaming sem carregar tudo em memória
    /// </summary>
    Task ProduceAsync(
        Channel<string> outputChannel,
        ListImportItemConfig config,
        CancellationToken cancellationToken);
}

/// <summary>
/// Consumidor de dados para Table Storage
/// Responsável por batch, rate limiting e inserção
/// </summary>
public interface IListImportConsumer
{
    /// <summary>
    /// Lê do channel, faz batch e insere em Table Storage
    /// Aplica rate limiting e resiliência
    /// </summary>
    Task ConsumeAsync(
        Channel<string> inputChannel,
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Orquestrador da importação
/// Coordena produtor e consumidor
/// </summary>
public interface IListImportOrchestrator
{
    /// <summary>
    /// Executa a importação completa (produtor + consumidor)
    /// </summary>
    Task<ImportMetrics> ExecuteImportAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Coletor de métricas de importação
/// Rastreia performance e velocidade em tempo real
/// </summary>
public interface IImportMetricsCollector
{
    /// <summary>
    /// Registra processamento de um item
    /// </summary>
    void RecordItemProcessed();

    /// <summary>
    /// Registra inserção bem-sucedida de um batch
    /// </summary>
    void RecordBatchSuccess(int itemCount, long elapsedMilliseconds);

    /// <summary>
    /// Registra falha em um batch
    /// </summary>
    void RecordBatchFailure(int itemCount, long elapsedMilliseconds);

    /// <summary>
    /// Retorna métricas atualizadas
    /// </summary>
    ImportMetrics GetCurrentMetrics();

    /// <summary>
    /// Reset das métricas
    /// </summary>
    void Reset();
}

/// <summary>
/// Repositório para armazenar/recuperar arquivos do blob
/// Mantém referência do último arquivo importado
/// </summary>
public interface IListBlobRepository
{
    /// <summary>
    /// Salva o arquivo de importação no blob
    /// </summary>
    Task<string> SaveImportFileAsync(
        string containerName,
        string blobName,
        Stream fileStream,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recupera arquivo anterior do blob
    /// </summary>
    Task<Stream?> GetPreviousImportFileAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Salva metadados sobre a importação
    /// </summary>
    Task SaveImportMetadataAsync(
        string containerName,
        string metadataName,
        ImportedListMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recupera metadados de importação anterior
    /// </summary>
    Task<ImportedListMetadata?> GetImportMetadataAsync(
        string containerName,
        string metadataName,
        CancellationToken cancellationToken);
}

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

/// <summary>
/// Rate limiter para controlar throughput de inserção
/// Evita throttling (429) do Table Storage
/// </summary>
public interface IImportRateLimiter
{
    /// <summary>
    /// Aguarda até que seja seguro executar operação
    /// Aplicar antes de cada batch
    /// </summary>
    Task WaitAsync(int itemCount, CancellationToken cancellationToken);

    /// <summary>
    /// Registra latência de uma operação
    /// Usado para ajustar rate limit dinamicamente
    /// </summary>
    void RecordOperationLatency(long elapsedMilliseconds);

    /// <summary>
    /// Retorna operações por segundo atual
    /// </summary>
    double GetCurrentOperationsPerSecond();
}

/// <summary>
/// Importador genérico para qualquer lista de domínios
/// Coordena todo o processo
/// </summary>
public interface IListImporter
{
    /// <summary>
    /// Executa importação completa de uma lista
    /// Lida com streaming, batching, rate limiting, resiliência
    /// </summary>
    Task<ImportMetrics> ImportAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executa diff entre arquivo anterior e novo
    /// Insere apenas mudanças (adds/removes)
    /// Mais eficiente que re-importar tudo
    /// </summary>
    Task<ImportMetrics> ImportDiffAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken);
}

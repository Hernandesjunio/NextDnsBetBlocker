namespace NextDnsBetBlocker.Core.Models;

/// <summary>
/// Configuração para importação de listas de domínios
/// </summary>
public class ListImportConfig
{
    /// <summary>
    /// Nome da lista (ex: TrancoList, HageziList)
    /// </summary>
    public required string ListName { get; set; }

    /// <summary>
    /// URL ou caminho da fonte de dados
    /// </summary>
    public required string SourceUrl { get; set; }

    /// <summary>
    /// Nome da tabela no Table Storage
    /// </summary>
    public required string TableName { get; set; }

    /// <summary>
    /// Container no Blob Storage para arquivo anterior
    /// </summary>
    public required string BlobContainer { get; set; }

    /// <summary>
    /// Tamanho do batch para inserção
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Número máximo de partições para sharding
    /// </summary>
    public int MaxPartitions { get; set; } = 10;

    /// <summary>
    /// Operações por segundo permitidas (rate limit)
    /// </summary>
    public int ThrottleOperationsPerSecond { get; set; } = 150000;

    /// <summary>
    /// Capacidade máxima do channel buffer
    /// </summary>
    public int ChannelCapacity { get; set; } = 10000;
}

/// <summary>
/// Métricas em tempo real da importação
/// </summary>
public class ImportMetrics
{
    /// <summary>
    /// Total de itens processados
    /// </summary>
    public long TotalProcessed { get; set; }

    /// <summary>
    /// Total de itens inseridos com sucesso
    /// </summary>
    public long TotalInserted { get; set; }

    /// <summary>
    /// Total de erros durante inserção
    /// </summary>
    public long TotalErrors { get; set; }

    /// <summary>
    /// Tempo decorrido desde o início
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// Taxa atual de itens por segundo
    /// </summary>
    public double ItemsPerSecond { get; set; }

    /// <summary>
    /// Taxa atual de operações por segundo (batches)
    /// </summary>
    public double OperationsPerSecond { get; set; }

    /// <summary>
    /// Latência média em ms por batch
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// Latência p95 em ms
    /// </summary>
    public double P95LatencyMs { get; set; }

    /// <summary>
    /// Latência p99 em ms
    /// </summary>
    public double P99LatencyMs { get; set; }

    /// <summary>
    /// Taxa de erro percentual
    /// </summary>
    public double ErrorRatePercent { get; set; }

    /// <summary>
    /// Itens estimados para processar
    /// </summary>
    public long EstimatedTotalItems { get; set; }

    /// <summary>
    /// Tempo estimado restante
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Tamanho atual do channel buffer
    /// </summary>
    public int ChannelCount { get; set; }

    /// <summary>
    /// Status atual da importação
    /// </summary>
    public ImportStatus Status { get; set; } = ImportStatus.Running;

    /// <summary>
    /// Mensagem de status
    /// </summary>
    public string StatusMessage { get; set; } = "Iniciando...";
}

/// <summary>
/// Status da importação
/// </summary>
public enum ImportStatus
{
    Pending = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

/// <summary>
/// Progresso detalhado da importação
/// </summary>
public class ImportProgress
{
    /// <summary>
    /// Identificador único da execução
    /// </summary>
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Métricas atualizadas
    /// </summary>
    public required ImportMetrics Metrics { get; set; }

    /// <summary>
    /// Timestamp da coleta de métricas
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Informações sobre o arquivo armazenado no blob
/// </summary>
public class ImportedListMetadata
{
    /// <summary>
    /// Nome da lista
    /// </summary>
    public required string ListName { get; set; }

    /// <summary>
    /// Hash SHA256 do arquivo
    /// </summary>
    public required string FileHash { get; set; }

    /// <summary>
    /// Total de registros no arquivo
    /// </summary>
    public long RecordCount { get; set; }

    /// <summary>
    /// Data da importação
    /// </summary>
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tamanho do arquivo em bytes
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Versão da fonte de dados
    /// </summary>
    public string? SourceVersion { get; set; }
}

/// <summary>
/// Resultado de uma operação de batch
/// </summary>
public class BatchOperationResult
{
    /// <summary>
    /// ID único do batch
    /// </summary>
    public string BatchId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Total de itens no batch
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Itens inseridos com sucesso
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Itens que falharam
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Tempo decorrido em ms
    /// </summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Exceção se houver falha
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Partições afetadas (sharding)
    /// </summary>
    public List<string> PartitionsAffected { get; set; } = [];

    /// <summary>
    /// Sucesso na operação
    /// </summary>
    public bool IsSuccess => FailureCount == 0;
}

/// <summary>
/// Entidade de domínio para Table Storage
/// </summary>
public class DomainListEntry
{
    /// <summary>
    /// Partition key (determinado por estratégia de hash)
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key (domínio)
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp da inserção
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// ETag para controle de versão
    /// </summary>
    public string? ETag { get; set; }
}

namespace NextDnsBetBlocker.Core.Models;

/// <summary>
/// Configurações para importação paralela com throttling por partição
/// Azure Table Storage limits: 20k ops/s total, 2k ops/s por partição
/// </summary>
public class ParallelImportConfig
{
    /// <summary>
    /// Grau máximo de paralelismo global (requests HTTP simultâneos)
    /// Controla o total de batches in-flight em TODAS as partições
    /// Recomendado: 50 (10 partições × 3-5 concurrent cada)
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 50;

    /// <summary>
    /// Batches concorrentes POR PARTIÇÃO (pipeline depth)
    /// Com 1: throughput limitado por latência HTTP (~100-500ms)
    /// Com 3: mascara latência, permite ~2k ops/s por partição
    /// Default: 3 (otimizado para latência típica de Azure Table Storage)
    /// </summary>
    public int MaxConcurrencyPerPartition { get; set; } = 3;

    /// <summary>
    /// Operações máximas por segundo POR PARTIÇÃO
    /// Azure Table Storage: 2.000 entities/s por partition key
    /// Safety net para garantir o limite mesmo com latência muito baixa
    /// </summary>
    public int MaxOpsPerSecondPerPartition { get; set; } = 2000;

    /// <summary>
    /// Operações máximas por segundo GLOBAL (todas as partições)
    /// Azure Table Storage: 20.000 entities/s por tabela (conta de storage)
    /// </summary>
    public int MaxOpsPerSecondGlobal { get; set; } = 20000;

    /// <summary>
    /// Capacidade do Channel bounded por partição (em batches)
    /// Controla backpressure: producer aguarda se channel estiver cheio
    /// Com 4.8M items / 10 partições / 100 items/batch = ~4,800 batches/partição
    /// Default 500: ~50k items buffer por partição antes de backpressure
    /// </summary>
    public int ChannelCapacityPerPartition { get; set; } = 500;

    /// <summary>
    /// Delay base em ms para backoff por partição quando recebe timeout/throttle
    /// Exponential: base × 2^attempt (1s, 2s, 4s, 8s...)
    /// Aplicado SOMENTE na partição com erro, sem afetar as demais
    /// </summary>
    public int PartitionBackoffBaseMs { get; set; } = 1000;

    /// <summary>
    /// Delay máximo em ms para backoff por partição
    /// Limita o crescimento exponencial do backoff
    /// </summary>
    public int PartitionBackoffMaxMs { get; set; } = 60000;

    /// <summary>
    /// Número máximo de retries por batch na partição antes de descartar
    /// Após exaurir retries, batch é marcado como falha e consumer continua
    /// </summary>
    public int MaxPartitionRetries { get; set; } = 5;

    /// <summary>
    /// Tamanho dos batches para processamento
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Número de workers de flush simultâneos por partição
    /// </summary>
    public int FlushWorkerCount { get; set; } = 20;

    /// <summary>
    /// Timeout para envio de um batch
    /// Se exceder, cancela e tenta retry (Polly no Orchestrator)
    /// </summary>
    public TimeSpan BatchTimeoutMs { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Intervalo em ms para report de progress
    /// </summary>
    public int ProgressReportIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Percentual de degradação por erro (0-100)
    /// </summary>
    public int DegradationPercentagePerError { get; set; } = 10;

    /// <summary>
    /// Percentual mínimo de degradação (0-100)
    /// </summary>
    public int MinimumDegradationPercentage { get; set; } = 80;

    /// <summary>
    /// Intervalo de recuperação em segundos
    /// </summary>
    public int RecoveryIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Intervalo de reset do circuit breaker em segundos
    /// </summary>
    public int CircuitBreakerResetIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Se a degradação adaptativa está habilitada
    /// </summary>
    public bool AdaptiveDegradationEnabled { get; set; } = true;
}

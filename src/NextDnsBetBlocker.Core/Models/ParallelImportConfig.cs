namespace NextDnsBetBlocker.Core.Models;

/// <summary>
/// Configurações para importação paralela
/// Otimizado para atingir 18k ops/s no Table Storage
/// </summary>
public class ParallelImportConfig
{
    /// <summary>
    /// Grau máximo de paralelismo (tasks simultâneas)
    /// Recomendado: 15 para atingir 18k ops/s
    /// Intervalo: 5-30 (teste e ajuste conforme ambiente)
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 15;

    /// <summary>
    /// Máximo de batches por partição na fila
    /// Evita explosão de memória com muitas partições
    /// </summary>
    public int MaxBatchesPerPartition { get; set; } = 5;

    /// <summary>
    /// Máximo de partições simultâneas em voo
    /// Table Storage suporta 20k ops/s POR PARTIÇÃO
    /// Com 2-3 partições: 18k total é seguro
    /// </summary>
    public int MaxPartitionsInFlight { get; set; } = 20;

    /// <summary>
    /// Timeout para envio de um batch
    /// Se exceder, cancela e tenta retry (Polly)
    /// </summary>
    public TimeSpan BatchTimeoutMs { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Usar rate limiting distribuído por partição
    /// Recomendado: true para melhor performance
    /// </summary>
    public bool UsePartitionRateLimiting { get; set; } = true;

    /// <summary>
    /// Operações máximas por segundo POR PARTIÇÃO
    /// Azure Table Storage: 20.000 ops/s por partição
    /// Usar 90% para margem de segurança
    /// </summary>
    public int MaxOpsPerSecondPerPartition { get; set; } = 18000;

    /// <summary>
    /// Intervalo em ms para report de progress
    /// </summary>
    public int ProgressReportIntervalMs { get; set; } = 5000;
}

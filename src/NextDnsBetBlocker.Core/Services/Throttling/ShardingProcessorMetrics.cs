namespace NextDnsBetBlocker.Core;

using System.Collections.Concurrent;

/// <summary>
/// Métricas do ShardingProcessor
/// Rastreia batches processados, throttling, degradação e circuit breaker
/// Similar à ParallelBatchManagerMetrics, mas com foco em taxa de transferência e resiliência
/// </summary>
public class ShardingProcessorMetrics
{
    private readonly ConcurrentDictionary<string, PartitionMetrics> _partitionMetrics;
    private volatile int _totalBatchesProcessed;
    private volatile int _totalBatchesFailed;
    private volatile int _totalThrottleWaits;
    private volatile int _totalDegradationEvents;
    private volatile int _totalCircuitBreakerOpenings;
    private volatile int _totalCircuitBreakerResets;
    private volatile int _totalItemsProcessed;

    /// <summary>
    /// Métricas por partição
    /// </summary>
    public class PartitionMetrics
    {
        public volatile int BatchesProcessed;
        public volatile int BatchesFailed;
        public volatile int ItemsProcessed;
        public volatile int ThrottleWaitCount;
        public volatile int DegradationCount;
        public volatile int CurrentDegradationPercentage; // 0-100 (100 = limite original)
        public volatile int CircuitBreakerOpeningCount;
        public volatile int CircuitBreakerResetCount;
        public volatile bool IsCircuitBreakerOpen;
        public int MaxBatchSize;
        public int MinBatchSize;
        public volatile int MaxThroughput;
    }

    public ShardingProcessorMetrics()
    {
        _partitionMetrics = new ConcurrentDictionary<string, PartitionMetrics>();
    }

    /// <summary>
    /// Registrar batch processado com sucesso
    /// </summary>
    public void RecordBatchProcessed(string partitionKey, int itemCount)
    {
        Interlocked.Increment(ref _totalBatchesProcessed);
        Interlocked.Add(ref _totalItemsProcessed, itemCount);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());
        Interlocked.Increment(ref metrics.BatchesProcessed);
        Interlocked.Add(ref metrics.ItemsProcessed, itemCount);

        // Rastrear min/max batch size
        if (itemCount > metrics.MaxBatchSize)
            metrics.MaxBatchSize = itemCount;

        if (itemCount < metrics.MinBatchSize || metrics.MinBatchSize == 0)
            metrics.MinBatchSize = itemCount;
    }

    /// <summary>
    /// Registrar falha de batch
    /// </summary>
    public void RecordBatchFailed(string partitionKey, int itemCount)
    {
        Interlocked.Increment(ref _totalBatchesFailed);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());
        Interlocked.Increment(ref metrics.BatchesFailed);
    }

    /// <summary>
    /// Registrar espera de throttle (rate limiting)
    /// </summary>
    public void RecordThrottleWait(string partitionKey, int waitTimeMs)
    {
        Interlocked.Increment(ref _totalThrottleWaits);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());
        Interlocked.Increment(ref metrics.ThrottleWaitCount);
    }

    /// <summary>
    /// Registrar evento de degradação (limite reduzido)
    /// </summary>
    public void RecordDegradation(string partitionKey, int currentDegradationPercentage)
    {
        Interlocked.Increment(ref _totalDegradationEvents);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());
        Interlocked.Increment(ref metrics.DegradationCount);
        Interlocked.Exchange(ref metrics.CurrentDegradationPercentage, currentDegradationPercentage);
    }

    /// <summary>
    /// Registrar abertura de circuit breaker
    /// </summary>
    public void RecordCircuitBreakerOpening(string partitionKey)
    {
        Interlocked.Increment(ref _totalCircuitBreakerOpenings);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());
        Interlocked.Increment(ref metrics.CircuitBreakerOpeningCount);
        Interlocked.Exchange(ref metrics.IsCircuitBreakerOpen, true);
    }

    /// <summary>
    /// Registrar reset de circuit breaker
    /// </summary>
    public void RecordCircuitBreakerReset(string partitionKey)
    {
        Interlocked.Increment(ref _totalCircuitBreakerResets);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());
        Interlocked.Increment(ref metrics.CircuitBreakerResetCount);
        Interlocked.Exchange(ref metrics.IsCircuitBreakerOpen, false);
    }

    /// <summary>
    /// Registrar throughput atual (ops/sec) para rastrear picos
    /// </summary>
    public void RecordThroughput(string partitionKey, int throughput)
    {
        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());

        // Loop otimista para atualizar o máximo (interlocked compare exchange pattern não é trivial para > comparision,
        // mas como throughput só aumenta em rajadas no mesmo segundo, e é resetado externamente (no worker),
        // aqui recebemos o "throughput do segundo atual".
        // Vamos apenas tentar atualizar se for maior.

        int currentMax = metrics.MaxThroughput;
        if (throughput > currentMax)
        {
            // Tenta atualizar se for maior. Se falhar (outro thread atualizou), não tem problema se perdermos um update menor.
            // Mas para garantir precisão do pico:
            int initialValue;
            do
            {
                initialValue = metrics.MaxThroughput;
                if (throughput <= initialValue) break;
            }
            while (Interlocked.CompareExchange(ref metrics.MaxThroughput, throughput, initialValue) != initialValue);
        }
    }

    /// <summary>
    /// Obter métricas consolidadas
    /// </summary>
    public ShardingProcessorMetricsSummary GetSummary()
    {
        var partitionSummaries = _partitionMetrics.ToDictionary(
            kvp => kvp.Key,
            kvp => new PartitionMetricsSummary
            {
                PartitionKey = kvp.Key,
                BatchesProcessed = kvp.Value.BatchesProcessed,
                BatchesFailed = kvp.Value.BatchesFailed,
                ItemsProcessed = kvp.Value.ItemsProcessed,
                ThrottleWaitCount = kvp.Value.ThrottleWaitCount,
                DegradationCount = kvp.Value.DegradationCount,
                CurrentDegradationPercentage = kvp.Value.CurrentDegradationPercentage,
                CircuitBreakerOpeningCount = kvp.Value.CircuitBreakerOpeningCount,
                CircuitBreakerResetCount = kvp.Value.CircuitBreakerResetCount,
                IsCircuitBreakerOpen = kvp.Value.IsCircuitBreakerOpen,
                MaxBatchSize = kvp.Value.MaxBatchSize,
                MinBatchSize = kvp.Value.MinBatchSize,
                MaxThroughput = kvp.Value.MaxThroughput,
                SuccessRate = kvp.Value.BatchesProcessed + kvp.Value.BatchesFailed > 0
                    ? (kvp.Value.BatchesProcessed * 100) / (kvp.Value.BatchesProcessed + kvp.Value.BatchesFailed)
                    : 0
            });

        return new ShardingProcessorMetricsSummary
        {
            TotalBatchesProcessed = _totalBatchesProcessed,
            TotalBatchesFailed = _totalBatchesFailed,
            TotalItemsProcessed = _totalItemsProcessed,
            TotalThrottleWaits = _totalThrottleWaits,
            TotalDegradationEvents = _totalDegradationEvents,
            TotalCircuitBreakerOpenings = _totalCircuitBreakerOpenings,
            TotalCircuitBreakerResets = _totalCircuitBreakerResets,
            PartitionMetrics = partitionSummaries,
            PartitionCount = _partitionMetrics.Count,
            GlobalSuccessRate = _totalBatchesProcessed + _totalBatchesFailed > 0
                ? (_totalBatchesProcessed * 100) / (_totalBatchesProcessed + _totalBatchesFailed)
                : 0
        };
    }

    /// <summary>
    /// Obter métricas de partição específica
    /// </summary>
    public PartitionMetricsSummary? GetPartitionMetrics(string partitionKey)
    {
        if (!_partitionMetrics.TryGetValue(partitionKey, out var metrics))
            return null;

        return new PartitionMetricsSummary
        {
            PartitionKey = partitionKey,
            BatchesProcessed = metrics.BatchesProcessed,
            BatchesFailed = metrics.BatchesFailed,
            ItemsProcessed = metrics.ItemsProcessed,
            ThrottleWaitCount = metrics.ThrottleWaitCount,
            DegradationCount = metrics.DegradationCount,
            CurrentDegradationPercentage = metrics.CurrentDegradationPercentage,
            CircuitBreakerOpeningCount = metrics.CircuitBreakerOpeningCount,
            CircuitBreakerResetCount = metrics.CircuitBreakerResetCount,
            IsCircuitBreakerOpen = metrics.IsCircuitBreakerOpen,
            MaxBatchSize = metrics.MaxBatchSize,
            MinBatchSize = metrics.MinBatchSize,
            MaxThroughput = metrics.MaxThroughput,
            SuccessRate = metrics.BatchesProcessed + metrics.BatchesFailed > 0
                ? (metrics.BatchesProcessed * 100) / (metrics.BatchesProcessed + metrics.BatchesFailed)
                : 0
        };
    }

    /// <summary>
    /// Resetar todas as métricas
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalBatchesProcessed, 0);
        Interlocked.Exchange(ref _totalBatchesFailed, 0);
        Interlocked.Exchange(ref _totalThrottleWaits, 0);
        Interlocked.Exchange(ref _totalDegradationEvents, 0);
        Interlocked.Exchange(ref _totalCircuitBreakerOpenings, 0);
        Interlocked.Exchange(ref _totalCircuitBreakerResets, 0);
        Interlocked.Exchange(ref _totalItemsProcessed, 0);
        _partitionMetrics.Clear();
    }
}

/// <summary>
/// Resumo de métricas globais do ShardingProcessor
/// </summary>
public class ShardingProcessorMetricsSummary
{
    public int TotalBatchesProcessed { get; set; }
    public int TotalBatchesFailed { get; set; }
    public int TotalItemsProcessed { get; set; }
    public int TotalThrottleWaits { get; set; }
    public int TotalDegradationEvents { get; set; }
    public int TotalCircuitBreakerOpenings { get; set; }
    public int TotalCircuitBreakerResets { get; set; }
    public int PartitionCount { get; set; }
    public int GlobalSuccessRate { get; set; }
    public Dictionary<string, PartitionMetricsSummary> PartitionMetrics { get; set; } = new();
}

/// <summary>
/// Resumo de métricas por partição
/// </summary>
public class PartitionMetricsSummary
{
    public string PartitionKey { get; set; } = string.Empty;
    public int BatchesProcessed { get; set; }
    public int BatchesFailed { get; set; }
    public int ItemsProcessed { get; set; }
    public int ThrottleWaitCount { get; set; }
    public int DegradationCount { get; set; }
    public int CurrentDegradationPercentage { get; set; }
    public int CircuitBreakerOpeningCount { get; set; }
    public int CircuitBreakerResetCount { get; set; }
    public bool IsCircuitBreakerOpen { get; set; }
    public int MaxBatchSize { get; set; }
    public int MinBatchSize { get; set; }
    public int MaxThroughput { get; set; }
    public int SuccessRate { get; set; }
}

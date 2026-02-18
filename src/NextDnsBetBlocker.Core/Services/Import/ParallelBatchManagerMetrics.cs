namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;

/// <summary>
/// Métricas do ParallelBatchManager
/// Rastreia enfileiramento, distribuição de load, backpressure, throttle e backoff
/// </summary>
public class ParallelBatchManagerMetrics
{
    private readonly ConcurrentDictionary<string, PartitionQueueMetrics> _partitionMetrics;
    private volatile int _totalItemsEnqueued;
    private volatile int _totalBatchesCreated;
    private volatile int _maxQueueDepth;
    private volatile int _backpressureEvents;
    private volatile int _totalThrottleDelays;
    private volatile int _totalBackoffEvents;
    private volatile int _totalRetriedBatches;
    private volatile int _totalDroppedBatches;

    public class PartitionQueueMetrics
    {
        public volatile int ItemsEnqueued;
        public volatile int BatchesCreated;
        public volatile int CurrentQueueDepth;
        public volatile int MaxQueueDepthReached;
        public volatile int BackpressureCount;
        public volatile int ThrottleDelayCount;
        public volatile int BackoffCount;
        public volatile int RetriedBatchCount;
        public volatile int DroppedBatchCount;
        public volatile int CurrentBackoffMs;
        public volatile int BatchesSucceeded;
        public volatile int BatchesFailed;
    }

    public ParallelBatchManagerMetrics()
    {
        _partitionMetrics = new ConcurrentDictionary<string, PartitionQueueMetrics>();
    }

    /// <summary>
    /// Registrar item enfileirado
    /// </summary>
    public void RecordItemEnqueued(string partitionKey)
    {
        Interlocked.Increment(ref _totalItemsEnqueued);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.ItemsEnqueued);
    }

    /// <summary>
    /// Registrar batch criado
    /// </summary>
    public void RecordBatchCreated(string partitionKey, int queueDepth)
    {
        Interlocked.Increment(ref _totalBatchesCreated);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.BatchesCreated);

        if (queueDepth > metrics.MaxQueueDepthReached)
        {
            Interlocked.Exchange(ref metrics.MaxQueueDepthReached, queueDepth);
        }

        if (queueDepth > _maxQueueDepth)
        {
            Interlocked.Exchange(ref _maxQueueDepth, queueDepth);
        }
    }

    /// <summary>
    /// Registrar evento de backpressure (fila cheia)
    /// </summary>
    public void RecordBackpressureEvent(string partitionKey)
    {
        Interlocked.Increment(ref _backpressureEvents);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.BackpressureCount);
    }

    /// <summary>
    /// Registrar delay de throttle por rate limiting
    /// </summary>
    public void RecordThrottleDelay(string partitionKey)
    {
        Interlocked.Increment(ref _totalThrottleDelays);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.ThrottleDelayCount);
    }

    /// <summary>
    /// Registrar evento de backoff por erro/timeout na partição
    /// </summary>
    public void RecordPartitionBackoff(string partitionKey, int currentBackoffMs)
    {
        Interlocked.Increment(ref _totalBackoffEvents);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.BackoffCount);
        Interlocked.Exchange(ref metrics.CurrentBackoffMs, currentBackoffMs);
    }

    /// <summary>
    /// Registrar batch retried (re-enfileirado após erro)
    /// </summary>
    public void RecordBatchRetried(string partitionKey)
    {
        Interlocked.Increment(ref _totalRetriedBatches);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.RetriedBatchCount);
    }

    /// <summary>
    /// Registrar batch descartado (exauriu retries)
    /// </summary>
    public void RecordBatchDropped(string partitionKey)
    {
        Interlocked.Increment(ref _totalDroppedBatches);

        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.DroppedBatchCount);
    }

    /// <summary>
    /// Registrar batch com sucesso na partição
    /// </summary>
    public void RecordBatchSucceeded(string partitionKey)
    {
        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.BatchesSucceeded);
    }

    /// <summary>
    /// Registrar batch com falha na partição
    /// </summary>
    public void RecordBatchFailed(string partitionKey)
    {
        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Increment(ref metrics.BatchesFailed);
    }

    /// <summary>
    /// Resetar backoff de uma partição (após sucesso)
    /// </summary>
    public void ResetPartitionBackoff(string partitionKey)
    {
        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        Interlocked.Exchange(ref metrics.CurrentBackoffMs, 0);
    }

    /// <summary>
    /// Atualizar profundidade atual de fila por partição
    /// </summary>
    public void UpdateQueueDepth(string partitionKey, int depth)
    {
        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionQueueMetrics());
        metrics.CurrentQueueDepth = depth;

        if (depth > metrics.MaxQueueDepthReached)
        {
            Interlocked.Exchange(ref metrics.MaxQueueDepthReached, depth);
        }
    }

    /// <summary>
    /// Obter métricas totais
    /// </summary>
    public (int TotalEnqueued, int TotalBatches, int MaxQueueDepth, int BackpressureEvents,
            int ThrottleDelays, int BackoffEvents, int RetriedBatches, int DroppedBatches) GetTotalMetrics()
    {
        return (
            _totalItemsEnqueued,
            _totalBatchesCreated,
            _maxQueueDepth,
            _backpressureEvents,
            _totalThrottleDelays,
            _totalBackoffEvents,
            _totalRetriedBatches,
            _totalDroppedBatches
        );
    }

    /// <summary>
    /// Obter distribuição de items por partição
    /// </summary>
    public Dictionary<string, int> GetItemsDistribution()
    {
        var distribution = new Dictionary<string, int>();
        foreach (var kvp in _partitionMetrics)
        {
            distribution[kvp.Key] = kvp.Value.ItemsEnqueued;
        }
        return distribution;
    }

    /// <summary>
    /// Obter distribuição de batches por partição
    /// </summary>
    public Dictionary<string, int> GetBatchesDistribution()
    {
        var distribution = new Dictionary<string, int>();
        foreach (var kvp in _partitionMetrics)
        {
            distribution[kvp.Key] = kvp.Value.BatchesCreated;
        }
        return distribution;
    }

    /// <summary>
    /// Obter profundidade de fila atual por partição
    /// </summary>
    public Dictionary<string, int> GetCurrentQueueDepths()
    {
        var depths = new Dictionary<string, int>();
        foreach (var kvp in _partitionMetrics)
        {
            depths[kvp.Key] = kvp.Value.CurrentQueueDepth;
        }
        return depths;
    }

    /// <summary>
    /// Obter métricas por partição
    /// </summary>
    public Dictionary<string, PartitionQueueMetrics> GetPartitionMetrics()
    {
        return new Dictionary<string, PartitionQueueMetrics>(_partitionMetrics);
    }

    /// <summary>
    /// Detectar desbalanceamento de carga
    /// Retorna true se diferença > 20%
    /// </summary>
    public bool HasLoadImbalance(out Dictionary<string, double> percentages)
    {
        percentages = new Dictionary<string, double>();

        if (_totalItemsEnqueued == 0)
            return false;

        var itemsPerPartition = GetItemsDistribution();
        if (itemsPerPartition.Count < 2)
            return false;

        foreach (var kvp in itemsPerPartition)
        {
            percentages[kvp.Key] = (kvp.Value / (double)_totalItemsEnqueued) * 100;
        }

        var maxPercentage = percentages.Values.Max();
        var minPercentage = percentages.Values.Min();
        var imbalance = maxPercentage - minPercentage;

        return imbalance > 20;
    }

    /// <summary>
    /// Obter estatísticas de backpressure
    /// </summary>
    public Dictionary<string, int> GetBackpressureStats()
    {
        var stats = new Dictionary<string, int>();
        foreach (var kvp in _partitionMetrics)
        {
            stats[kvp.Key] = kvp.Value.BackpressureCount;
        }
        return stats;
    }

    /// <summary>
    /// Obter estatísticas de backoff por partição
    /// </summary>
    public Dictionary<string, (int BackoffCount, int CurrentBackoffMs, int RetriedBatches, int DroppedBatches)> GetBackoffStats()
    {
        var stats = new Dictionary<string, (int, int, int, int)>();
        foreach (var kvp in _partitionMetrics)
        {
            var m = kvp.Value;
            stats[kvp.Key] = (m.BackoffCount, m.CurrentBackoffMs, m.RetriedBatchCount, m.DroppedBatchCount);
        }
        return stats;
    }
}

namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;

/// <summary>
/// Métricas do ParallelBatchManager
/// Rastreia enfileiramento, distribuição de load e backpressure
/// </summary>
public class ParallelBatchManagerMetrics
{
    private readonly ConcurrentDictionary<string, PartitionQueueMetrics> _partitionMetrics;
    private volatile int _totalItemsEnqueued;
    private volatile int _totalBatchesCreated;
    private volatile int _maxQueueDepth;
    private volatile int _backpressureEvents;

    public class PartitionQueueMetrics
    {
        // Usar volatile fields para thread-safety com Interlocked
        public volatile int ItemsEnqueued;
        public volatile int BatchesCreated;
        public volatile int CurrentQueueDepth;
        public volatile int MaxQueueDepthReached;
        public volatile int BackpressureCount;
    }

    public ParallelBatchManagerMetrics()
    {
        _partitionMetrics = new ConcurrentDictionary<string, PartitionQueueMetrics>();
        _totalItemsEnqueued = 0;
        _totalBatchesCreated = 0;
        _maxQueueDepth = 0;
        _backpressureEvents = 0;
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

        // Atualizar profundidade de fila
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
    public (int TotalEnqueued, int TotalBatches, int MaxQueueDepth, int BackpressureEvents) GetTotalMetrics()
    {
        return (
            _totalItemsEnqueued,
            _totalBatchesCreated,
            _maxQueueDepth,
            _backpressureEvents
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

        // Verificar se alguma partição tem mais de 60% ou menos de 40%
        var maxPercentage = percentages.Values.Max();
        var minPercentage = percentages.Values.Min();
        var imbalance = maxPercentage - minPercentage;

        return imbalance > 20;  // Mais de 20% de diferença = desbalanceamento
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
}

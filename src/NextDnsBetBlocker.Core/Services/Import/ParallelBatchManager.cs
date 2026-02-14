namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Gerenciador de batches paralelos com INSTRUMENTAÇÃO
/// Agrupa por partição, controla paralelismo e rastreia métricas
/// </summary>
public class ParallelBatchManager : IDisposable
{
    private readonly ParallelImportConfig _config;
    private readonly ConcurrentDictionary<string, PartitionBatchQueue> _batchQueues;
    private readonly SemaphoreSlim _maxParallelismSemaphore;
    private readonly ConcurrentBag<Task> _activeTasks;
    private readonly ParallelBatchManagerMetrics _metrics;

    private class PartitionBatchQueue
    {
        public List<DomainListEntry> CurrentBatch { get; set; } = new();
        public Queue<List<DomainListEntry>> PendingBatches { get; set; } = new();
        public int ItemCount { get; set; }
    }

    public ParallelBatchManager(ParallelImportConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _batchQueues = new ConcurrentDictionary<string, PartitionBatchQueue>();
        _maxParallelismSemaphore = new SemaphoreSlim(_config.MaxDegreeOfParallelism, _config.MaxDegreeOfParallelism);
        _activeTasks = new ConcurrentBag<Task>();
        _metrics = new ParallelBatchManagerMetrics();
    }

    /// <summary>
    /// Enfileirar uma entrada (domínio)
    /// Agrupa automaticamente por partição com rastreamento
    /// </summary>
    public void Enqueue(DomainListEntry entry)
    {
        var partitionKey = entry.PartitionKey;
        var queue = _batchQueues.GetOrAdd(partitionKey, _ => new PartitionBatchQueue());

        _metrics.RecordItemEnqueued(partitionKey);

        lock (queue)
        {
            queue.CurrentBatch.Add(entry);
            queue.ItemCount++;

            // Se batch completo, mover para fila de pendentes
            if (queue.CurrentBatch.Count >= _config.MaxBatchesPerPartition * 100)
            {
                queue.PendingBatches.Enqueue(new List<DomainListEntry>(queue.CurrentBatch));
                _metrics.RecordBatchCreated(partitionKey, queue.PendingBatches.Count);
                queue.CurrentBatch.Clear();

                // Registrar backpressure se fila está crescendo
                if (queue.PendingBatches.Count >= _config.MaxBatchesPerPartition * 0.8)
                {
                    _metrics.RecordBackpressureEvent(partitionKey);
                }
            }

            // Atualizar profundidade de fila
            _metrics.UpdateQueueDepth(partitionKey, queue.PendingBatches.Count);
        }
    }

    /// <summary>
    /// Flush: enviar todos os batches pendentes em paralelo
    /// </summary>
    public async Task FlushAsync(
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken = default)
    {
        // Mover batches finais para fila
        foreach (var kvp in _batchQueues)
        {
            var queue = kvp.Value;
            lock (queue)
            {
                if (queue.CurrentBatch.Count > 0)
                {
                    queue.PendingBatches.Enqueue(new List<DomainListEntry>(queue.CurrentBatch));
                    _metrics.RecordBatchCreated(kvp.Key, queue.PendingBatches.Count);
                    queue.CurrentBatch.Clear();
                }
            }
        }

        // Processar todos os batches em paralelo
        var tasks = new List<Task>();

        foreach (var kvp in _batchQueues)
        {
            var partitionKey = kvp.Key;
            var queue = kvp.Value;

            // Dequeue e processar batches desta partição
            while (true)
            {
                List<DomainListEntry>? batch;
                lock (queue)
                {
                    if (queue.PendingBatches.Count == 0)
                        break;
                    batch = queue.PendingBatches.Dequeue();
                    _metrics.UpdateQueueDepth(partitionKey, queue.PendingBatches.Count);
                }

                if (batch == null || batch.Count == 0)
                    continue;

                // Aguardar semáforo (limite de paralelismo)
                await _maxParallelismSemaphore.WaitAsync(cancellationToken);

                // Criar task para este batch
                var task = ProcessBatchAsync(batch, sendBatchFunc, cancellationToken);
                tasks.Add(task);
                _activeTasks.Add(task);
            }
        }

        // Aguardar todas as tasks
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Processar um batch e liberar semáforo ao final
    /// </summary>
    private async Task ProcessBatchAsync(
        List<DomainListEntry> batch,
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken)
    {
        try
        {
            await sendBatchFunc(batch);
        }
        finally
        {
            // Liberar semáforo para próxima task
            _maxParallelismSemaphore.Release();
        }
    }

    /// <summary>
    /// Obter número de tasks ativas
    /// </summary>
    public int GetActiveTaskCount()
    {
        return _activeTasks.Count(t => !t.IsCompleted);
    }

    /// <summary>
    /// Obter número de batches pendentes por partição
    /// </summary>
    public Dictionary<string, int> GetPendingBatchCounts()
    {
        var counts = new Dictionary<string, int>();

        foreach (var kvp in _batchQueues)
        {
            lock (kvp.Value)
            {
                counts[kvp.Key] = kvp.Value.PendingBatches.Count;
            }
        }

        return counts;
    }

    /// <summary>
    /// Obter total de itens enfileirados
    /// </summary>
    public int GetTotalQueuedItems()
    {
        return _batchQueues.Values.Sum(q => q.ItemCount);
    }

    /// <summary>
    /// Obter métricas do gerenciador
    /// </summary>
    public ParallelBatchManagerMetrics GetMetrics()
    {
        return _metrics;
    }

    public void Dispose()
    {
        _maxParallelismSemaphore?.Dispose();
    }
}

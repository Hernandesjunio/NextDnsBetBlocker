namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Gerenciador de batches paralelos com INSTRUMENTAÇÃO
/// Agrupa por partição, controla paralelismo e rastreia métricas
/// Thread-safe: Usa ConcurrentQueue para Phase 2 (multi-threaded)
/// Phase 1 (Enqueue) é single-threaded, sem lock necessário
/// </summary>
public class ParallelBatchManager : IDisposable
{
    private readonly ParallelImportConfig _config;
    private readonly ConcurrentDictionary<string, PartitionBatchQueue> _batchQueues;
    private readonly SemaphoreSlim _maxParallelismSemaphore;
    private readonly ConcurrentBag<Task> _activeTasks;
    private readonly ParallelBatchManagerMetrics _metrics;
    private volatile int _totalBatchesProcessed;

    private class PartitionBatchQueue
    {
        public List<DomainListEntry> CurrentBatch { get; set; } = new();
        public ConcurrentQueue<List<DomainListEntry>> PendingBatches { get; set; } = new();  // ✅ ConcurrentQueue (thread-safe)
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
    /// NOTA: Chamado apenas durante Phase 1 (single-threaded producer)
    /// Sem lock necessário - sequencial
    /// </summary>
    public void Enqueue(DomainListEntry entry)
    {
        var partitionKey = entry.PartitionKey;
        var queue = _batchQueues.GetOrAdd(partitionKey, _ => new PartitionBatchQueue());

        _metrics.RecordItemEnqueued(partitionKey);

        // ✅ SEM LOCK: Phase 1 é single-threaded (producer sequencial)
        queue.CurrentBatch.Add(entry);
        queue.ItemCount++;

        // Se batch atingiu 100 items, mover para fila de pendentes
        if (queue.CurrentBatch.Count >= 100)
        {
            queue.PendingBatches.Enqueue(new List<DomainListEntry>(queue.CurrentBatch));
            _metrics.RecordBatchCreated(partitionKey, queue.PendingBatches.Count);
            queue.CurrentBatch.Clear();

            // Registrar backpressure se fila está crescendo
            if (queue.PendingBatches.Count >= 10)
            {
                _metrics.RecordBackpressureEvent(partitionKey);
            }
        }

        // Atualizar profundidade de fila
        _metrics.UpdateQueueDepth(partitionKey, queue.PendingBatches.Count);
    }

    /// <summary>
    /// Flush: enviar todos os batches pendentes em paralelo
    /// Phase 2 é multi-threaded, ConcurrentQueue é thread-safe
    /// </summary>
    public async Task FlushAsync(
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken = default)
    {
        // Mover batches finais para fila
        foreach (var kvp in _batchQueues)
        {
            var queue = kvp.Value;

            // ✅ SEM LOCK: CurrentBatch é per-partition, não compartilhado
            if (queue.CurrentBatch.Count > 0)
            {
                queue.PendingBatches.Enqueue(new List<DomainListEntry>(queue.CurrentBatch));
                _metrics.RecordBatchCreated(kvp.Key, queue.PendingBatches.Count);
                queue.CurrentBatch.Clear();
            }
        }

        // Processar todos os batches em paralelo
        var tasks = new List<Task>();
        var lastProgressReport = 0;
        var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var kvp in _batchQueues)
        {
            var partitionKey = kvp.Key;
            var queue = kvp.Value;

            // Dequeue e processar batches desta partição
            // ✅ ConcurrentQueue é thread-safe, TryDequeue é atomico
            while (queue.PendingBatches.TryDequeue(out var batch))
            {
                if (batch == null || batch.Count == 0)
                    continue;

                // Aguardar semáforo (limite de paralelismo)
                await _maxParallelismSemaphore.WaitAsync(cancellationToken);

                // Criar task para este batch
                var task = ProcessBatchAsync(batch, partitionKey, sendBatchFunc, cancellationToken);
                tasks.Add(task);
                _activeTasks.Add(task);

                // Log progresso de forma eficiente (a cada N batches)
                _totalBatchesProcessed++;
                if (_totalBatchesProcessed % 500 == 0)  // A cada 500 batches = ~50k items
                {
                    var percentComplete = (_totalBatchesProcessed * 100) / GetTotalBatches();
                    var itemsProcessed = _totalBatchesProcessed * 100;  // ~100 items/batch
                    var elapsed = progressStopwatch.ElapsedMilliseconds;
                    var throughput = elapsed > 0 ? (itemsProcessed / (elapsed / 1000.0)) : 0;

                    // Log apenas a cada segundo
                    if (elapsed - lastProgressReport > 1000)
                    {
                        lastProgressReport = (int)elapsed;
                        // Logging será feito no ListImportConsumer via PerformanceMonitor
                    }
                }
            }
        }

        // Aguardar todas as tasks
        await Task.WhenAll(tasks);
        progressStopwatch.Stop();
    }

    private int GetTotalBatches()
    {
        return _batchQueues.Values.Sum(q => q.ItemCount / 100 + (q.ItemCount % 100 > 0 ? 1 : 0));
    }

    /// <summary>
    /// Processar um batch e liberar semáforo ao final
    /// </summary>
    private async Task ProcessBatchAsync(
        List<DomainListEntry> batch,
        string partitionKey,
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken)
    {
        try
        {
            await sendBatchFunc(batch);
            _metrics.UpdateQueueDepth(partitionKey, GetQueueDepth(partitionKey));
        }
        finally
        {
            // Liberar semáforo para próxima task
            _maxParallelismSemaphore.Release();
        }
    }

    private int GetQueueDepth(string partitionKey)
    {
        if (_batchQueues.TryGetValue(partitionKey, out var queue))
        {
            return queue.PendingBatches.Count;
        }
        return 0;
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
    /// ✅ ConcurrentQueue tem Count thread-safe
    /// </summary>
    public Dictionary<string, int> GetPendingBatchCounts()
    {
        var counts = new Dictionary<string, int>();

        foreach (var kvp in _batchQueues)
        {
            // ✅ SEM LOCK: ConcurrentQueue.Count é thread-safe
            counts[kvp.Key] = kvp.Value.PendingBatches.Count;
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

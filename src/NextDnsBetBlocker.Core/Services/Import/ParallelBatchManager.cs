namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Gerenciador de batches paralelos com LOGGING COMPLETO
/// Agrupa por partição, controla paralelismo, rastreia métricas e loga progresso
/// Thread-safe: Usa ConcurrentQueue para Phase 2 (multi-threaded)
/// Phase 1 (Enqueue) é single-threaded, sem lock necessário
/// </summary>
public class ParallelBatchManager : IDisposable
{
    private readonly ParallelImportConfig _config;
    private readonly ILogger<ParallelBatchManager> _logger;
    private readonly ConcurrentDictionary<string, PartitionBatchQueue> _batchQueues;
    private readonly SemaphoreSlim _maxParallelismSemaphore;
    private readonly ConcurrentBag<Task> _activeTasks;
    private readonly ParallelBatchManagerMetrics _metrics;
    private volatile int _totalBatchesProcessed;
    private int _totalBatches;

    private class PartitionBatchQueue
    {
        public List<DomainListEntry> CurrentBatch { get; set; } = new();
        public ConcurrentQueue<List<DomainListEntry>> PendingBatches { get; set; } = new();  // ✅ ConcurrentQueue (thread-safe)
        public int ItemCount { get; set; }
    }

    public ParallelBatchManager(
        ParallelImportConfig config,
        ILogger<ParallelBatchManager> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batchQueues = new ConcurrentDictionary<string, PartitionBatchQueue>();
        _maxParallelismSemaphore = new SemaphoreSlim(_config.MaxDegreeOfParallelism, _config.MaxDegreeOfParallelism);
        _activeTasks = new ConcurrentBag<Task>();
        _metrics = new ParallelBatchManagerMetrics();
        _totalBatchesProcessed = 0;
        _totalBatches = 0;
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
    /// Loga progresso em tempo real
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

        // Calcular total de batches para estimar progresso
        _totalBatches = GetTotalBatches();
        _logger.LogInformation("[Phase 2] Starting parallel flush with {MaxDegreeOfParallelism} concurrent tasks | {TotalBatches} batches to process",
            _config.MaxDegreeOfParallelism, _totalBatches);

        // Processar todos os batches em paralelo
        var tasks = new List<Task>();
        var lastProgressReport = DateTime.UtcNow;
        var lastBatchesReported = 0;
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

                // Log progresso de forma eficiente
                _totalBatchesProcessed++;

                // Log a cada 500 batches (~50k items) e a cada 5 segundos
                if (_totalBatchesProcessed % 500 == 0)
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressReport).TotalSeconds >= 5)
                    {
                        LogFlushProgress(progressStopwatch.Elapsed, lastBatchesReported);
                        lastProgressReport = now;
                        lastBatchesReported = _totalBatchesProcessed;
                    }
                }
            }
        }

        // Aguardar todas as tasks
        _logger.LogInformation("[Phase 2] All {Count} batches enqueued, waiting for {ActiveTasks} tasks to complete...", 
            _totalBatchesProcessed, GetActiveTaskCount());

        await Task.WhenAll(tasks);
        progressStopwatch.Stop();

        // Log final
        _logger.LogInformation("[Phase 2] ✓ Completed | Processed {Batches} batches | Throughput: {Throughput:F0} ops/s | Time: {Time}",
            _totalBatchesProcessed,
            (_totalBatchesProcessed * 100) / progressStopwatch.Elapsed.TotalSeconds,
            FormatTimeSpan(progressStopwatch.Elapsed));
    }

    private void LogFlushProgress(TimeSpan elapsed, int lastBatchesReported)
    {
        var itemsProcessed = _totalBatchesProcessed * 100;  // ~100 items/batch
        var itemsLastReported = lastBatchesReported * 100;
        var itemsSinceLastReport = itemsProcessed - itemsLastReported;

        var percentComplete = _totalBatches > 0 ? (_totalBatchesProcessed * 100) / _totalBatches : 0;
        var throughput = elapsed.TotalSeconds > 0 ? itemsProcessed / elapsed.TotalSeconds : 0;

        // Estimar ETA
        var itemsPerSecond = itemsSinceLastReport / Math.Max(1, elapsed.TotalSeconds);
        var itemsRemaining = (_totalBatches - _totalBatchesProcessed) * 100;
        var secondsRemaining = itemsPerSecond > 0 ? itemsRemaining / itemsPerSecond : 0;
        var eta = TimeSpan.FromSeconds(secondsRemaining);

        _logger.LogInformation(
            "[Phase 2] ✓ {Percent}% complete ({Processed:N0} items) | Throughput: {Throughput:F0} ops/s | ETA: {ETA}",
            percentComplete,
            itemsProcessed,
            throughput,
            FormatTimeSpan(eta));
    }

    private string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        else if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        else
            return $"{ts.Seconds}s";
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

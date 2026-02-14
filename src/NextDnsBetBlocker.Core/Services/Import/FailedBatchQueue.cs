namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Fila de batches falhados com timeout
/// Armazenamento em memória para reprocessamento posterior
/// Thread-safe: ConcurrentQueue
/// </summary>
public class FailedBatchQueue
{
    private readonly ConcurrentQueue<FailedBatchEntry> _queue = new();
    private volatile int _totalFailed = 0;
    private volatile int _totalRetried = 0;

    public class FailedBatchEntry
    {
        public List<DomainListEntry> Batch { get; set; } = new();
        public string PartitionKey { get; set; } = string.Empty;
        public int AttemptCount { get; set; } = 1;
        public DateTime FirstFailedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastFailedAt { get; set; } = DateTime.UtcNow;
        public string LastErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Adicionar batch falhado à fila
    /// </summary>
    public void Enqueue(List<DomainListEntry> batch, string partitionKey, string errorMessage)
    {
        _queue.Enqueue(new FailedBatchEntry
        {
            Batch = batch,
            PartitionKey = partitionKey,
            FirstFailedAt = DateTime.UtcNow,
            LastFailedAt = DateTime.UtcNow,
            LastErrorMessage = errorMessage
        });
        Interlocked.Increment(ref _totalFailed);
    }

    /// <summary>
    /// Registrar nova tentativa
    /// </summary>
    public void RecordRetry(FailedBatchEntry entry, string errorMessage)
    {
        entry.AttemptCount++;
        entry.LastFailedAt = DateTime.UtcNow;
        entry.LastErrorMessage = errorMessage;
        Interlocked.Increment(ref _totalRetried);
    }

    /// <summary>
    /// Obter próximo batch para reprocessar
    /// </summary>
    public bool TryDequeue(out FailedBatchEntry? entry) => _queue.TryDequeue(out entry);

    /// <summary>
    /// Obter todos os batches falhados sem remover
    /// </summary>
    public List<FailedBatchEntry> GetAll() => _queue.ToList();

    /// <summary>
    /// Número de batches ainda na fila
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Obter estatísticas
    /// </summary>
    public (int TotalFailed, int TotalRetried, int Remaining) GetStats() 
        => (_totalFailed, _totalRetried, _queue.Count);

    /// <summary>
    /// Limpar fila
    /// </summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}

namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// Rate limiter distribuído por partição
/// Monitora throughput de cada partição separadamente
/// Implementa backpressure automática por partição
/// </summary>
public class PartitionRateLimiter
{
    private readonly int _maxOpsPerSecond;
    private readonly ConcurrentDictionary<string, PartitionMetrics> _partitionMetrics;
    private readonly object _lockObject = new();

    private class PartitionMetrics
    {
        public Queue<long> OperationTimestamps { get; set; } = new();
        public long TotalOperations { get; set; }
        public long TotalLatencyMs { get; set; }
        public Stopwatch Stopwatch { get; set; } = Stopwatch.StartNew();
    }

    public PartitionRateLimiter(int maxOpsPerSecond)
    {
        if (maxOpsPerSecond <= 0)
            throw new ArgumentException("Max ops per second must be positive", nameof(maxOpsPerSecond));

        _maxOpsPerSecond = maxOpsPerSecond;
        _partitionMetrics = new ConcurrentDictionary<string, PartitionMetrics>();
    }

    /// <summary>
    /// Aguarda até ser seguro enviar operação para esta partição
    /// Implementa sliding window de 1 segundo por partição
    /// </summary>
    public async Task WaitAsync(string partitionKey, int itemCount, CancellationToken cancellationToken)
    {
        var minDelayMs = CalculateMinDelay(partitionKey, itemCount);

        if (minDelayMs > 0)
        {
            // Para latências pequenas: usar SpinWait (preciso)
            // Para latências grandes: usar Task.Delay (economiza CPU)
            if (minDelayMs < 5)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < minDelayMs)
                {
                    // Busy-wait (preciso)
                }
            }
            else
            {
                await Task.Delay(minDelayMs, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Registra latência de operação para esta partição
    /// Atualiza métricas e sliding window
    /// </summary>
    public void RecordOperationLatency(string partitionKey, long elapsedMilliseconds, int itemCount)
    {
        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());

        lock (_lockObject)
        {
            var now = metrics.Stopwatch.ElapsedMilliseconds;
            metrics.OperationTimestamps.Enqueue(now);
            metrics.TotalOperations += itemCount;
            metrics.TotalLatencyMs += elapsedMilliseconds;

            // Remover timestamps fora da janela de 1 segundo
            while (metrics.OperationTimestamps.Count > 0 && (now - metrics.OperationTimestamps.Peek()) > 1000)
            {
                metrics.OperationTimestamps.Dequeue();
            }
        }
    }

    /// <summary>
    /// Obter throughput atual para uma partição (ops/s)
    /// </summary>
    public double GetCurrentOperationsPerSecond(string partitionKey)
    {
        if (!_partitionMetrics.TryGetValue(partitionKey, out var metrics))
            return 0;

        lock (_lockObject)
        {
            return metrics.OperationTimestamps.Count;
        }
    }

    /// <summary>
    /// Obter latência média para uma partição
    /// </summary>
    public double GetAverageLatencyMs(string partitionKey)
    {
        if (!_partitionMetrics.TryGetValue(partitionKey, out var metrics))
            return 0;

        lock (_lockObject)
        {
            if (metrics.TotalOperations == 0)
                return 0;

            return (double)metrics.TotalLatencyMs / metrics.TotalOperations;
        }
    }

    /// <summary>
    /// Obter métricas de todas as partições
    /// </summary>
    public Dictionary<string, PartitionStats> GetAllPartitionStats()
    {
        var stats = new Dictionary<string, PartitionStats>();

        foreach (var kvp in _partitionMetrics)
        {
            lock (_lockObject)
            {
                stats[kvp.Key] = new PartitionStats
                {
                    PartitionKey = kvp.Key,
                    CurrentOpsPerSecond = kvp.Value.OperationTimestamps.Count,
                    TotalOperations = kvp.Value.TotalOperations,
                    AverageLatencyMs = kvp.Value.TotalOperations > 0
                        ? (double)kvp.Value.TotalLatencyMs / kvp.Value.TotalOperations
                        : 0
                };
            }
        }

        return stats;
    }

    private int CalculateMinDelay(string partitionKey, int itemCount)
    {
        var metrics = _partitionMetrics.GetOrAdd(partitionKey, _ => new PartitionMetrics());

        lock (_lockObject)
        {
            var now = metrics.Stopwatch.ElapsedMilliseconds;

            // Limpar operações fora da janela de 1 segundo
            while (metrics.OperationTimestamps.Count > 0 && (now - metrics.OperationTimestamps.Peek()) > 1000)
            {
                metrics.OperationTimestamps.Dequeue();
            }

            // Se não excedeu limite, permitir imediato
            if (metrics.OperationTimestamps.Count + itemCount <= _maxOpsPerSecond)
            {
                return 0;
            }

            // Calcular tempo até próxima operação ser permitida
            var oldestOperation = metrics.OperationTimestamps.Peek();
            var timeUntilExpiry = (int)((oldestOperation + 1000 - now) * 1.1); // +10% buffer

            return Math.Max(1, timeUntilExpiry);
        }
    }
}

/// <summary>
/// Estatísticas de uma partição
/// </summary>
public class PartitionStats
{
    public string PartitionKey { get; set; } = string.Empty;
    public double CurrentOpsPerSecond { get; set; }
    public long TotalOperations { get; set; }
    public double AverageLatencyMs { get; set; }
}

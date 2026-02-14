namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// Monitor de performance em tempo real
/// Rastreia throughput, latência, taxa de erro e ETA
/// </summary>
public class PerformanceMonitor
{
    private readonly Stopwatch _overallStopwatch;
    private readonly int _totalItems;
    private volatile int _processedItems;
    private volatile int _failedItems;
    private readonly ConcurrentQueue<long> _latencies;
    private readonly int _maxLatencyHistorySize;
    private volatile int _lastReportTime;  // ← int ao invés de long (volatile suporta int/ref)
    private volatile int _lastReportedItems;

    public PerformanceMonitor(int totalItems)
    {
        _totalItems = totalItems;
        _processedItems = 0;
        _failedItems = 0;
        _maxLatencyHistorySize = 10000;  // Manter últimas 10k latências
        _latencies = new ConcurrentQueue<long>();
        _overallStopwatch = Stopwatch.StartNew();
        _lastReportTime = (int)_overallStopwatch.ElapsedMilliseconds;
        _lastReportedItems = 0;
    }

    /// <summary>
    /// Registrar latência de uma operação
    /// </summary>
    public void RecordLatency(long elapsedMilliseconds)
    {
        _latencies.Enqueue(elapsedMilliseconds);

        // Manter apenas as últimas X latências para não explodir memória
        if (_latencies.Count > _maxLatencyHistorySize)
        {
            _latencies.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Incrementar contador de items processados
    /// </summary>
    public void IncrementProcessed(int count = 1)
    {
        Interlocked.Add(ref _processedItems, count);
    }

    /// <summary>
    /// Incrementar contador de items falhados
    /// </summary>
    public void IncrementFailed(int count = 1)
    {
        Interlocked.Add(ref _failedItems, count);
    }

    /// <summary>
    /// Obter throughput atual (ops/s)
    /// </summary>
    public double GetCurrentThroughput()
    {
        if (_overallStopwatch.ElapsedMilliseconds == 0)
            return 0;

        return (_processedItems / (double)_overallStopwatch.ElapsedMilliseconds) * 1000;
    }

    /// <summary>
    /// Obter throughput desde o último report (mais preciso para período curto)
    /// </summary>
    public double GetRecentThroughput()
    {
        var now = (int)_overallStopwatch.ElapsedMilliseconds;
        var timeDiffMs = now - _lastReportTime;

        if (timeDiffMs == 0)
            return 0;

        var itemsDiff = _processedItems - _lastReportedItems;
        return (itemsDiff / (double)timeDiffMs) * 1000;
    }

    /// <summary>
    /// Atualizar baseline de report para cálculo de throughput recente
    /// </summary>
    public void UpdateReportBaseline()
    {
        _lastReportTime = (int)_overallStopwatch.ElapsedMilliseconds;
        _lastReportedItems = _processedItems;
    }

    /// <summary>
    /// Obter latência média (ms)
    /// </summary>
    public double GetAverageLatency()
    {
        if (_latencies.Count == 0)
            return 0;

        return _latencies.Average();
    }

    /// <summary>
    /// Obter percentil de latência (p50, p95, p99, etc)
    /// </summary>
    public long GetLatencyPercentile(double percentile)
    {
        if (_latencies.Count == 0)
            return 0;

        var sorted = _latencies.OrderBy(x => x).ToList();
        var index = (int)((percentile / 100) * (sorted.Count - 1));
        return sorted[Math.Max(0, index)];
    }

    /// <summary>
    /// Obter taxa de erro (%)
    /// </summary>
    public double GetErrorRate()
    {
        if (_processedItems == 0)
            return 0;

        return (_failedItems / (double)_processedItems) * 100;
    }

    /// <summary>
    /// Obter tempo estimado restante (TimeSpan)
    /// </summary>
    public TimeSpan GetEstimatedTimeRemaining()
    {
        var throughput = GetCurrentThroughput();
        if (throughput <= 0)
            return TimeSpan.Zero;

        var itemsRemaining = _totalItems - _processedItems;
        var secondsRemaining = itemsRemaining / throughput;

        return TimeSpan.FromSeconds(secondsRemaining);
    }

    /// <summary>
    /// Obter tempo total decorrido
    /// </summary>
    public TimeSpan GetElapsedTime()
    {
        return _overallStopwatch.Elapsed;
    }

    /// <summary>
    /// Obter progresso (0-100%)
    /// </summary>
    public double GetProgress()
    {
        if (_totalItems == 0)
            return 0;

        return (_processedItems / (double)_totalItems) * 100;
    }

    /// <summary>
    /// Obter estatísticas completas
    /// </summary>
    public PerformanceStats GetStats()
    {
        return new PerformanceStats
        {
            ProcessedItems = _processedItems,
            FailedItems = _failedItems,
            TotalItems = _totalItems,
            Progress = GetProgress(),
            CurrentThroughput = GetCurrentThroughput(),
            RecentThroughput = GetRecentThroughput(),
            AverageLatency = GetAverageLatency(),
            P50Latency = GetLatencyPercentile(50),
            P95Latency = GetLatencyPercentile(95),
            P99Latency = GetLatencyPercentile(99),
            P999Latency = GetLatencyPercentile(99.9),
            ErrorRate = GetErrorRate(),
            ElapsedTime = GetElapsedTime(),
            EstimatedTimeRemaining = GetEstimatedTimeRemaining()
        };
    }
}

/// <summary>
/// Estatísticas de performance
/// </summary>
public class PerformanceStats
{
    public int ProcessedItems { get; set; }
    public int FailedItems { get; set; }
    public int TotalItems { get; set; }
    public double Progress { get; set; }
    public double CurrentThroughput { get; set; }
    public double RecentThroughput { get; set; }
    public double AverageLatency { get; set; }
    public long P50Latency { get; set; }
    public long P95Latency { get; set; }
    public long P99Latency { get; set; }
    public long P999Latency { get; set; }
    public double ErrorRate { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
}

namespace NextDnsBetBlocker.Core.Services.Import;

using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Coletor de métricas de importação em tempo real
/// Rastreia performance, velocidade e latência
/// Thread-safe para operações paralelas
/// </summary>
public class ImportMetricsCollector : IImportMetricsCollector
{
    private readonly object _lockObject = new();
    private long _totalProcessed;
    private long _totalInserted;
    private long _totalErrors;
    private long _totalBatches;
    private long _totalBatchesErrors;
    private long _totalElapsedBatchMs;
    private readonly Queue<long> _recentLatencies;
    private const int MaxRecentLatencies = 1000;
    private readonly Stopwatch _stopwatch;

    public ImportMetricsCollector()
    {
        _stopwatch = Stopwatch.StartNew();
        _recentLatencies = new Queue<long>(MaxRecentLatencies);
    }

    public void RecordItemProcessed()
    {
        lock (_lockObject)
        {
            _totalProcessed++;
        }
    }

    public void RecordBatchSuccess(int itemCount, long elapsedMilliseconds)
    {
        lock (_lockObject)
        {
            _totalInserted += itemCount;
            _totalBatches++;
            _totalElapsedBatchMs += elapsedMilliseconds;
            
            // Manter histórico de latências para p95/p99
            if (_recentLatencies.Count >= MaxRecentLatencies)
                _recentLatencies.Dequeue();
            
            _recentLatencies.Enqueue(elapsedMilliseconds);
        }
    }

    public void RecordBatchFailure(int itemCount, long elapsedMilliseconds)
    {
        lock (_lockObject)
        {
            _totalErrors += itemCount;
            _totalBatchesErrors++;
            _totalElapsedBatchMs += elapsedMilliseconds;
            
            if (_recentLatencies.Count >= MaxRecentLatencies)
                _recentLatencies.Dequeue();
            
            _recentLatencies.Enqueue(elapsedMilliseconds);
        }
    }

    public ImportMetrics GetCurrentMetrics()
    {
        lock (_lockObject)
        {
            var elapsed = _stopwatch.Elapsed;
            var elapsedSeconds = elapsed.TotalSeconds;

            // Calcular taxas
            var itemsPerSecond = elapsedSeconds > 0 ? _totalProcessed / elapsedSeconds : 0;
            var operationsPerSecond = elapsedSeconds > 0 ? _totalBatches / elapsedSeconds : 0;
            var averageLatencyMs = _totalBatches > 0 ? _totalElapsedBatchMs / (double)_totalBatches : 0;
            var errorRate = (_totalInserted + _totalErrors) > 0 
                ? (_totalErrors / (double)(_totalInserted + _totalErrors)) * 100 
                : 0;

            // Calcular percentis
            var (p95, p99) = CalculatePercentiles();

            return new ImportMetrics
            {
                TotalProcessed = _totalProcessed,
                TotalInserted = _totalInserted,
                TotalErrors = _totalErrors,
                ElapsedTime = elapsed,
                ItemsPerSecond = itemsPerSecond,
                OperationsPerSecond = operationsPerSecond,
                AverageLatencyMs = averageLatencyMs,
                P95LatencyMs = p95,
                P99LatencyMs = p99,
                ErrorRatePercent = errorRate
            };
        }
    }

    public void Reset()
    {
        lock (_lockObject)
        {
            _totalProcessed = 0;
            _totalInserted = 0;
            _totalErrors = 0;
            _totalBatches = 0;
            _totalBatchesErrors = 0;
            _totalElapsedBatchMs = 0;
            _recentLatencies.Clear();
            _stopwatch.Restart();
        }
    }

    private (double p95, double p99) CalculatePercentiles()
    {
        if (_recentLatencies.Count == 0)
            return (0, 0);

        var sorted = _recentLatencies.OrderBy(x => x).ToList();
        var p95Index = (int)(sorted.Count * 0.95);
        var p99Index = (int)(sorted.Count * 0.99);

        var p95 = sorted[Math.Max(0, p95Index)];
        var p99 = sorted[Math.Max(0, p99Index)];

        return (p95, p99);
    }
}

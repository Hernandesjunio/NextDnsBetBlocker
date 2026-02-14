namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using System.Text;

/// <summary>
/// Logger de performance estruturado
/// Responsável por logging em tempo real de progresso e métricas
/// </summary>
public class PerformanceLogger
{
    private readonly ILogger _logger;
    private readonly string _listName;
    private long _lastLogTime;
    private int _lastLoggedItems;

    public PerformanceLogger(ILogger logger, string listName)
    {
        _logger = logger;
        _listName = listName;
        _lastLogTime = 0;
        _lastLoggedItems = 0;
    }

    /// <summary>
    /// Log de progresso com intervalo mínimo
    /// </summary>
    public void LogProgress(PerformanceStats stats, long intervalMs = 5000)
    {
        var now = Environment.TickCount64;

        // Não log com frequência excessiva
        if (now - _lastLogTime < intervalMs)
            return;

        _lastLogTime = now;

        var sb = new StringBuilder();
        sb.Append($"[{_listName}] Progress: ");
        sb.Append($"{stats.ProcessedItems:N0}/{stats.TotalItems:N0} items ({stats.Progress:F1}%) | ");
        sb.Append($"Throughput: {stats.RecentThroughput:F0} ops/s | ");
        sb.Append($"Latency: {stats.AverageLatency:F1}ms (p95: {stats.P95Latency}ms, p99: {stats.P99Latency}ms) | ");
        sb.Append($"Errors: {stats.ErrorRate:F2}% | ");
        sb.Append($"ETA: {FormatTimeSpan(stats.EstimatedTimeRemaining)}");

        _logger.LogInformation(sb.ToString());
    }

    /// <summary>
    /// Log de progresso por percentual (1%, 5%, 10%, etc)
    /// </summary>
    public void LogProgressPercentile(PerformanceStats stats, int percentileInterval = 5)
    {
        var currentPercentile = (int)stats.Progress;
        var lastPercentile = (int)((_lastLoggedItems / (double)stats.TotalItems) * 100);

        if (currentPercentile >= lastPercentile + percentileInterval || currentPercentile == 100)
        {
            _lastLoggedItems = stats.ProcessedItems;

            _logger.LogInformation(
                "[{ListName}] ✓ {Progress}% complete ({Processed:N0}/{Total:N0} items) - Throughput: {Throughput:F0} ops/s - ETA: {ETA}",
                _listName,
                currentPercentile,
                stats.ProcessedItems,
                stats.TotalItems,
                stats.RecentThroughput,
                FormatTimeSpan(stats.EstimatedTimeRemaining));
        }
    }

    /// <summary>
    /// Log de métricas por partição
    /// </summary>
    public void LogPartitionMetrics(
        string partitionKey,
        double throughput,
        double avgLatency,
        long p95Latency,
        long p99Latency,
        int totalItems)
    {
        _logger.LogInformation(
            "[{ListName}] Partition {Partition}: {Throughput:F0} ops/s | Items: {Total:N0} | AvgLatency: {AvgLat:F1}ms | p95: {P95}ms | p99: {P99}ms",
            _listName,
            partitionKey,
            throughput,
            totalItems,
            avgLatency,
            p95Latency,
            p99Latency);
    }

    /// <summary>
    /// Log de alerta de degradação
    /// </summary>
    public void LogPerformanceDegradation(
        double currentThroughput,
        double expectedThroughput,
        double degradationPercent)
    {
        _logger.LogWarning(
            "[{ListName}] ⚠ Performance degradation detected: {Current:F0} ops/s (expected: {Expected:F0} ops/s) - {Degradation:F1}% below expected",
            _listName,
            currentThroughput,
            expectedThroughput,
            degradationPercent);
    }

    /// <summary>
    /// Log de alerta de latência alta
    /// </summary>
    public void LogHighLatency(long p99Latency, long threshold)
    {
        if (p99Latency > threshold)
        {
            _logger.LogWarning(
                "[{ListName}] ⚠ High latency detected: p99 = {P99}ms (threshold: {Threshold}ms)",
                _listName,
                p99Latency,
                threshold);
        }
    }

    /// <summary>
    /// Log de alerta de taxa de erro alta
    /// </summary>
    public void LogHighErrorRate(double errorRate, double threshold)
    {
        if (errorRate > threshold)
        {
            _logger.LogWarning(
                "[{ListName}] ⚠ High error rate detected: {ErrorRate:F2}% (threshold: {Threshold:F2}%)",
                _listName,
                errorRate,
                threshold);
        }
    }

    /// <summary>
    /// Log de resumo final
    /// </summary>
    public void LogCompletionSummary(PerformanceStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"╔═══════════════════════════════════════════════════════════════╗");
        sb.AppendLine($"║ Import Completed: {_listName,-51} ║");
        sb.AppendLine($"╠═══════════════════════════════════════════════════════════════╣");
        sb.AppendLine($"║ Total Items: {stats.ProcessedItems:N0,-48} ║");
        sb.AppendLine($"║ Failed Items: {stats.FailedItems:N0,-47} ║");
        sb.AppendLine($"║ Error Rate: {stats.ErrorRate:F2}%{string.Empty,-46} ║");
        sb.AppendLine($"║ Elapsed Time: {FormatTimeSpan(stats.ElapsedTime),-47} ║");
        sb.AppendLine($"║ Throughput: {stats.CurrentThroughput:F0} ops/s{string.Empty,-43} ║");
        sb.AppendLine($"║ Avg Latency: {stats.AverageLatency:F1}ms{string.Empty,-46} ║");
        sb.AppendLine($"║ P95 Latency: {stats.P95Latency}ms{string.Empty,-45} ║");
        sb.AppendLine($"║ P99 Latency: {stats.P99Latency}ms{string.Empty,-45} ║");
        sb.AppendLine($"╚═══════════════════════════════════════════════════════════════╝");

        _logger.LogInformation(sb.ToString());
    }

    /// <summary>
    /// Log de resumo de partições
    /// </summary>
    public void LogPartitionsSummary(Dictionary<string, PartitionSummary> partitionStats)
    {
        if (partitionStats.Count == 0)
            return;

        _logger.LogInformation("[{ListName}] Partition Summary:", _listName);
        foreach (var kvp in partitionStats)
        {
            _logger.LogInformation(
                "[{ListName}]   {Partition}: {Throughput:F0} ops/s | {Items:N0} items | AvgLat: {AvgLatency:F1}ms | p99: {P99}ms",
                _listName,
                kvp.Key,
                kvp.Value.Throughput,
                kvp.Value.TotalItems,
                kvp.Value.AverageLatency,
                kvp.Value.P99Latency);
        }
    }

    /// <summary>
    /// Log de distribuição de load por partição
    /// </summary>
    public void LogLoadDistribution(Dictionary<string, int> itemsPerPartition)
    {
        if (itemsPerPartition.Count == 0)
            return;

        var total = itemsPerPartition.Values.Sum();

        _logger.LogInformation("[{ListName}] Load Distribution:", _listName);
        foreach (var kvp in itemsPerPartition.OrderByDescending(x => x.Value))
        {
            var percentage = (kvp.Value / (double)total) * 100;
            var bar = new string('█', (int)(percentage / 2));
            _logger.LogInformation(
                "[{ListName}]   {Partition}: {Items:N0} items ({Percentage:F1}%) {Bar}",
                _listName,
                kvp.Key,
                kvp.Value,
                percentage,
                bar);
        }
    }

    /// <summary>
    /// Formatar TimeSpan para formato legível
    /// </summary>
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        else if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        else
            return $"{ts.Seconds}s";
    }
}

/// <summary>
/// Resumo de performance de uma partição
/// </summary>
public class PartitionSummary
{
    public string PartitionKey { get; set; } = string.Empty;
    public double Throughput { get; set; }
    public int TotalItems { get; set; }
    public double AverageLatency { get; set; }
    public long P99Latency { get; set; }
}

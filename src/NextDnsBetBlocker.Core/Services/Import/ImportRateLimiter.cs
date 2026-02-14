namespace NextDnsBetBlocker.Core.Services.Import;

using NextDnsBetBlocker.Core.Interfaces;
using System.Diagnostics;

/// <summary>
/// Rate limiter com sliding window
/// Controla throughput para evitar throttling (429) do Table Storage
/// Implementa backpressure automática
/// </summary>
public class ImportRateLimiter : IImportRateLimiter
{
    private readonly int _maxOperationsPerSecond;
    private readonly Queue<long> _operationTimestamps;
    private readonly object _lockObject = new();
    private readonly Stopwatch _stopwatch;
    private long _totalOperations;

    public ImportRateLimiter(int maxOperationsPerSecond)
    {
        if (maxOperationsPerSecond <= 0)
            throw new ArgumentException("Max operations per second must be positive", nameof(maxOperationsPerSecond));

        _maxOperationsPerSecond = maxOperationsPerSecond;
        _operationTimestamps = new Queue<long>();
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Aguarda até que seja seguro enviar operação
    /// Usa sliding window de 1 segundo
    /// </summary>
    public async Task WaitAsync(int itemCount, CancellationToken cancellationToken)
    {
        var minDelayMs = CalculateMinDelay(itemCount);
        
        if (minDelayMs > 0)
        {
            await Task.Delay(minDelayMs, cancellationToken);
        }
    }

    public void RecordOperationLatency(long elapsedMilliseconds)
    {
        lock (_lockObject)
        {
            var now = _stopwatch.ElapsedMilliseconds;
            _operationTimestamps.Enqueue(now);
            _totalOperations++;

            // Remover timestamps antigos (fora da janela de 1 segundo)
            while (_operationTimestamps.Count > 0 && (now - _operationTimestamps.Peek()) > 1000)
            {
                _operationTimestamps.Dequeue();
            }
        }
    }

    public double GetCurrentOperationsPerSecond()
    {
        lock (_lockObject)
        {
            return _operationTimestamps.Count;
        }
    }

    private int CalculateMinDelay(int itemCount)
    {
        lock (_lockObject)
        {
            var now = _stopwatch.ElapsedMilliseconds;

            // Limpar operações fora da janela de 1 segundo
            while (_operationTimestamps.Count > 0 && (now - _operationTimestamps.Peek()) > 1000)
            {
                _operationTimestamps.Dequeue();
            }

            // Se não excedeu limite, permitir imediato
            if (_operationTimestamps.Count < _maxOperationsPerSecond)
            {
                return 0;
            }

            // Calcular tempo até a próxima operação ser permitida
            var oldestOperation = _operationTimestamps.Peek();
            var timeUntilExpiry = (int)((oldestOperation + 1000 - now) * 1.1); // +10% buffer
            
            return Math.Max(1, timeUntilExpiry);
        }
    }
}

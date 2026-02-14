namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;

/// <summary>
/// Controlador de paralelismo adaptativo
/// Reduz 5% a 5% quando detecta timeout
/// Mínimo: 5 tasks paralelas
/// </summary>
public class AdaptiveParallelismController
{
    private readonly ILogger _logger;
    private int _currentDegreeOfParallelism;
    private readonly int _minDegreeOfParallelism = 5;
    private readonly int _initialDegreeOfParallelism;
    private long _timeoutCount = 0;
    private long _successCount = 0;
    private const decimal REDUCTION_PERCENTAGE = 0.05m; // 5%

    public AdaptiveParallelismController(ILogger logger, int initialDegreeOfParallelism)
    {
        _logger = logger;
        _currentDegreeOfParallelism = initialDegreeOfParallelism;
        _initialDegreeOfParallelism = initialDegreeOfParallelism;
    }

    /// <summary>
    /// Obter grau atual de paralelismo
    /// </summary>
    public int GetCurrentDegreeOfParallelism() => _currentDegreeOfParallelism;

    /// <summary>
    /// Registrar timeout e reduzir paralelismo
    /// </summary>
    public void RecordTimeout()
    {
        Interlocked.Increment(ref _timeoutCount);
        
        // Calcular redução de 5%
        var newDegree = (int)(_currentDegreeOfParallelism * (1 - REDUCTION_PERCENTAGE));
        newDegree = Math.Max(newDegree, _minDegreeOfParallelism);
        
        if (newDegree < _currentDegreeOfParallelism)
        {
            _logger.LogWarning(
                "[Adaptive] ⚠ Timeout detected! Reducing parallelism by 5%: {Old} tasks → {New} tasks",
                _currentDegreeOfParallelism, newDegree);
            
            _currentDegreeOfParallelism = newDegree;
        }
    }

    /// <summary>
    /// Registrar sucesso
    /// </summary>
    public void RecordSuccess()
    {
        Interlocked.Increment(ref _successCount);
    }

    /// <summary>
    /// Obter estatísticas
    /// </summary>
    public (int Timeouts, int Successes, int Current, int Initial) GetStats()
    {
        return (
            (int)Interlocked.Read(ref _timeoutCount),
            (int)Interlocked.Read(ref _successCount),
            _currentDegreeOfParallelism,
            _initialDegreeOfParallelism
        );
    }
}

namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Rate limiter para controlar throughput de inserção
/// Evita throttling (429) do Table Storage
/// </summary>
public interface IImportRateLimiter
{
    /// <summary>
    /// Aguarda até que seja seguro executar operação
    /// Aplicar antes de cada batch
    /// </summary>
    Task WaitAsync(int itemCount, CancellationToken cancellationToken);

    /// <summary>
    /// Registra latência de uma operação
    /// Usado para ajustar rate limit dinamicamente
    /// </summary>
    void RecordOperationLatency(long elapsedMilliseconds);

    /// <summary>
    /// Retorna operações por segundo atual
    /// </summary>
    double GetCurrentOperationsPerSecond();
}

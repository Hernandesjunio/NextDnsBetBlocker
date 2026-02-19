namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Coletor de métricas de importação
/// Rastreia performance e velocidade em tempo real
/// </summary>
public interface IImportMetricsCollector
{
    /// <summary>
    /// Registra processamento de um item
    /// </summary>
    void RecordItemProcessed();

    /// <summary>
    /// Registra inserção bem-sucedida de um batch
    /// </summary>
    void RecordBatchSuccess(int itemCount, long elapsedMilliseconds);

    /// <summary>
    /// Registra falha em um batch
    /// </summary>
    void RecordBatchFailure(int itemCount, long elapsedMilliseconds);

    /// <summary>
    /// Retorna métricas atualizadas
    /// </summary>
    ImportMetrics GetCurrentMetrics();

    /// <summary>
    /// Reset das métricas
    /// </summary>
    void Reset();
}

namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Publisher para enviar eventos de domínios suspeitos para Azure Storage Queue
/// Custo mínimo: ~$0.0001 por 1M operações
/// </summary>
public interface ISuspectDomainQueuePublisher
{
    /// <summary>
    /// Publica um domínio suspeito para análise na fila
    /// </summary>
    Task PublishAsync(SuspectDomainQueueMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publica múltiplos domínios em batch (mais eficiente)
    /// </summary>
    Task PublishBatchAsync(IEnumerable<SuspectDomainQueueMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Testa a conexão com a fila
    /// </summary>
    Task TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna estatísticas da fila
    /// </summary>
    Task<QueueStatistics> GetQueueStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Estatísticas da fila
/// </summary>
public class QueueStatistics
{
    /// <summary>
    /// Aproximadamente quantas mensagens estão na fila
    /// </summary>
    public int ApproximateMessageCount { get; set; }

    /// <summary>
    /// Quando foi último acesso
    /// </summary>
    public DateTime LastAccessedTime { get; set; }

    /// <summary>
    /// Quando a fila foi criada
    /// </summary>
    public DateTime CreatedTime { get; set; }
}

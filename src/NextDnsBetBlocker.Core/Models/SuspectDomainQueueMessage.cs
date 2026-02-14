namespace NextDnsBetBlocker.Core.Models;

/// <summary>
/// Evento de domínio suspeito para ser publicado na Storage Queue
/// Serializado como JSON para consumo por Azure Functions
/// </summary>
public class SuspectDomainQueueMessage
{
    /// <summary>
    /// Domínio suspeito a ser analisado
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Profile ID do NextDNS
    /// </summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>
    /// Primeiro momento em que foi visto
    /// </summary>
    public DateTime FirstSeen { get; set; }

    /// <summary>
    /// Score de classificação (0-100)
    /// Definido pelo ClassifierConsumer
    /// </summary>
    public double ClassificationScore { get; set; }

    /// <summary>
    /// ID único para rastreamento
    /// </summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp de quando foi enfileirado
    /// </summary>
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Source: qual serviço gerou (BetBlockerPipeline, etc)
    /// </summary>
    public string Source { get; set; } = "BetBlockerPipeline";
}

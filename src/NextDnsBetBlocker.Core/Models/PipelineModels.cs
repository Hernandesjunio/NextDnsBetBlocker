namespace NextDnsBetBlocker.Core.Models;

/// <summary>
/// DTO para passagem de logs no canal produtor-consumidor
/// Representa um domínio individual do log do NextDNS
/// </summary>
public class LogEntryData
{
    public required string Domain { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ProfileId { get; set; }
}

/// <summary>
/// DTO para passagem de domínios suspeitos entre consumidores
/// Após classificação inicial, apenas domínios suspeitos são passados
/// </summary>
public class SuspectDomainEntry
{
    public required string Domain { get; set; }
    public DateTime FirstSeen { get; set; }
    public string? ProfileId { get; set; }
    public int ClassificationScore { get; set; } // Score do BetClassifier
}

/// <summary>
/// Estatísticas de pipeline em tempo real
/// Usado para rastreabilidade e debugging
/// </summary>
public class PipelineStats
{
    public DateTime StartTime { get; set; }
    public int LogsProduced { get; set; }
    public int LogsClassified { get; set; }
    public int SuspectsIdentified { get; set; }
    public int SuspectsAnalyzed { get; set; }
    public int DomainsBlocked { get; set; }
    public int ChannelBufferCount { get; set; }
}

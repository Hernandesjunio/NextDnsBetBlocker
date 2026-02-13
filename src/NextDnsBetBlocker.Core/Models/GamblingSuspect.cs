namespace NextDnsBetBlocker.Core.Models;

public class GamblingSuspect
{
    public string Domain { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public int AccessCount { get; set; }
    public AnalysisStatus Status { get; set; }
    public int ConfidenceScore { get; set; } // 0-100
    public List<string> GamblingIndicators { get; set; } = [];
    public int DomainAgeInDays { get; set; }
    public DateTime? LastAnalyzed { get; set; }
    public string BlockReason { get; set; } = string.Empty;
    public bool IsWhitelisted { get; set; }
    public string SslIssuer { get; set; } = string.Empty;
    public DateTime? SslExpiryDate { get; set; }
    public int SuspiciousDnsRecords { get; set; }
    public string AnalysisDetails { get; set; } = string.Empty;
}

public enum AnalysisStatus
{
    Pending,
    InProgress,
    Completed,
    Whitelisted,
    Blocked,
    Manual_Review
}

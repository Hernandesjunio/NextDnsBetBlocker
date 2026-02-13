namespace NextDnsBetBlocker.Core.Models;

public class AnalysisResult
{
    public string Domain { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; }
    public List<AnalysisIndicator> Indicators { get; set; } = [];
    public bool IsGambling { get; set; }
    public DateTime AnalyzedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AnalysisIndicator
{
    public string Category { get; set; } = string.Empty; // "Content", "Domain", "SSL", "WHOIS", "DNS"
    public string Indicator { get; set; } = string.Empty; // ex: "keyword:aposta", "age:7days", "issuer:suspicious"
    public int Score { get; set; } // Weight 0-100
    public string Description { get; set; } = string.Empty;
}

namespace NextDnsBetBlocker.Core.Models;

public class NextDnsLogsResponse
{
    public List<LogEntry> Data { get; set; } = [];
    public PaginationMeta Meta { get; set; } = new();
}

public class LogEntry
{
    public string Domain { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class PaginationMeta
{
    public PaginationInfo Pagination { get; set; } = new();
}

public class PaginationInfo
{
    public string? Cursor { get; set; }
}

public class DenylistBlockRequest
{
    public string Id { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
}

public class BlockerRunStatistics
{
    public int DomainsLogged { get; set; }
    public int UniqueDomains { get; set; }
    public int DomainsBlocked { get; set; }
    public int DomainsSkipped { get; set; }
    public int DomainsAllowlisted { get; set; }
    public int DomainsAlreadyBlocked { get; set; }
    public long HageziTotalDomains { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

public class AgentStateEntity
{
    public string ProfileId { get; set; } = string.Empty;
    public DateTime? LastTimestamp { get; set; }
}

public class BlockedDomainEntity
{
    public string ProfileId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; }
}

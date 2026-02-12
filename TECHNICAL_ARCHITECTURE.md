# Technical Architecture

## System Design

```
┌─────────────────────────────────────────────────────────────────┐
│                      NextDNS Bet Blocker                         │
└─────────────────────────────────────────────────────────────────┘

External Services:
├── NextDNS API (GET logs, POST denylist)
├── HaGeZi GitHub (Daily blocklist update)
└── Azure/Azurite (Table Storage, Blob Storage)

┌─────────────────────────────────────────────────────────────────┐
│                    Worker / Function App                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────────┐     ┌──────────────────┐                 │
│  │ Timer Triggers   │     │ Timer Triggers   │                 │
│  │ Every 30 min     │     │ Daily @ 00:00    │                 │
│  └────────┬─────────┘     └────────┬─────────┘                 │
│           │                        │                             │
│           v                        v                             │
│  ┌────────────────────┐   ┌──────────────────┐                 │
│  │ProcessLogsFunction │   │UpdateHaGeziFunc  │                 │
│  └────────┬───────────┘   └────────┬─────────┘                 │
│           │                        │                             │
│           v                        v                             │
│  ┌─────────────────────────────────────────┐                   │
│  │      IBetBlockerPipeline                 │                   │
│  │  ┌───────────────────────────────────┐  │                   │
│  │  │ 1. Load Allowlist                 │  │                   │
│  │  │ 2. Get Checkpoint                 │  │                   │
│  │  │ 3. Fetch NextDNS Logs             │  │                   │
│  │  │ 4. Normalize & Deduplicate        │  │                   │
│  │  │ 5. Load HaGeZi List               │  │                   │
│  │  │ 6. Classify & Block Domains       │  │                   │
│  │  │ 7. Update Checkpoint              │  │                   │
│  │  │ 8. Log Statistics                 │  │                   │
│  │  └───────────────────────────────────┘  │                   │
│  └─────────────────────────────────────────┘                   │
│           │                                                      │
│  ┌────────┴─────────────────────────────────────┐               │
│  │          Service Layer                        │               │
│  ├──────────────────────────────────────────────┤               │
│  │ INextDnsClient       │ ICheckpointStore      │               │
│  │ IBlockedDomainStore  │ IHageziProvider       │               │
│  │ IAllowlistProvider   │ IBetClassifier        │               │
│  └──────────────────────────────────────────────┘               │
│           │                                                      │
│  ┌────────┴─────────────────────────────────────┐               │
│  │          Storage Layer                        │               │
│  ├──────────────────────────────────────────────┤               │
│  │ Azure Table Storage  │ Azure Blob Storage    │               │
│  │ (Checkpoint & Blocked) │ (HaGeZi Cache)     │               │
│  └──────────────────────────────────────────────┘               │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

## Data Flow

### Log Processing Pipeline

```
1. LOAD STATE
   └─ Retrieve last processed timestamp from AgentState table
      (or None if first run)

2. FETCH LOGS (Paginated)
   ├─ GET /profiles/{profileId}/logs?limit=1000&sort=asc
   ├─ Extract domains from response
   ├─ Filter: only logs with timestamp > last checkpoint
   ├─ Retrieve next page using cursor
   └─ Repeat until no more pages

3. NORMALIZE DOMAINS
   ├─ Convert to lowercase
   ├─ Trim whitespace
   ├─ Remove trailing dot
   └─ Deduplicate (HashSet)

4. CLASSIFY DOMAINS
   ├─ Load HaGeZi HashSet from memory/cache
   ├─ For each domain:
   │  ├─ Check exact match in HaGeZi
   │  ├─ Check parent domains (e.g., example.com for bet.example.com)
   │  └─ Mark if found
   └─ Get list of classified domains

5. FILTER & BLOCK
   ├─ Skip if in allowlist
   ├─ Skip if already blocked (check BlockedDomains table)
   ├─ POST /profiles/{profileId}/denylist for each domain
   ├─ Apply rate limiting (5 req/s default)
   ├─ On success: Mark in BlockedDomains table
   └─ Record statistics

6. UPDATE CHECKPOINT
   └─ Save latest processed timestamp to AgentState table

7. RETURN STATISTICS
   ├─ Duration
   ├─ Domains logged/unique/blocked/skipped/allowlisted
   ├─ HaGeZi size
   └─ Log all metrics
```

## Storage Schema

### Azure Table Storage

#### Table: AgentState
```
PartitionKey: "checkpoint"
RowKey: <ProfileId>
Fields:
  - LastTimestamp (DateTime): ISO 8601 formatted UTC timestamp
  
Example:
  PartitionKey: "checkpoint"
  RowKey: "abc123def456"
  LastTimestamp: "2024-01-15T14:30:00.0000000Z"
```

#### Table: BlockedDomains
```
PartitionKey: <ProfileId>
RowKey: <Domain> (normalized: lowercase, trimmed, no trailing dot)
Fields:
  - BlockedAt (DateTime): When the domain was blocked

Example:
  PartitionKey: "abc123def456"
  RowKey: "betsite.example.com"
  BlockedAt: "2024-01-15T14:32:15.1234567Z"
```

### Blob Storage

#### Container: blocklists
```
Blob Name: hagezi-gambling-domains.txt
Format: Adblock Plus list format (||domain.com^)
Size: ~200KB (45k+ domains)
Update Frequency: Daily (midnight UTC)
```

### Local File Storage (Development)

```
./data/
├── hagezi-gambling-domains.txt  # HaGeZi cache
├── blocked-domains.txt          # Format: profileId|domain|timestamp
└── checkpoints.txt              # Format: profileId|timestamp

./src/NextDnsBetBlocker.Worker/
└── allowlist.txt                # One domain per line, # for comments
```

## API Integration

### NextDNS GET /profiles/{profileId}/logs

**Request:**
```bash
GET https://api.nextdns.io/profiles/{profileId}/logs?limit=1000&sort=asc&cursor=...
Header: X-Api-Key: {apiKey}
```

**Response:**
```json
{
  "data": [
    {
      "domain": "example.com",
      "timestamp": "2024-01-15T14:30:00.000Z",
      ...
    }
  ],
  "meta": {
    "pagination": {
      "cursor": "eyJvZmZzZXQiOjEwMDB9"
    }
  }
}
```

**Pagination Logic:**
- Start with no cursor (or checkpoint-based cursor)
- Process all entries with timestamp > last checkpoint
- Continue while cursor is present
- Update checkpoint with latest timestamp
- Next run starts from updated checkpoint

### NextDNS POST /profiles/{profileId}/denylist

**Request:**
```bash
POST https://api.nextdns.io/profiles/{profileId}/denylist
Header: X-Api-Key: {apiKey}
Content-Type: application/json
Body: {
  "id": "betting-site.com",
  "active": true
}
```

**Response:**
```json
{
  "id": "betting-site.com",
  "active": true,
  "createdAt": "2024-01-15T14:32:15.000Z"
}
```

## Error Handling & Retry Strategy

### Retry Policy

**Conditions for Retry:**
- HTTP 429 (Too Many Requests)
- HTTP 5xx (Server Errors)
- Network timeouts

**Backoff Strategy:**
```
Attempt 1: Immediate
Attempt 2: 1000ms delay
Attempt 3: 2000ms delay
Attempt 4: 4000ms delay
Attempt 5: 8000ms delay
Max Delay: 30000ms (30 seconds)
Max Retries: 5
```

### Rate Limiting

**Implementation:**
```csharp
delayBetweenRequests = TimeSpan.FromMilliseconds(1000.0 / rateLimitPerSecond);
// Default: 1000ms / 5 = 200ms between requests
// Results in: max 5 requests/second
```

**Configuration:**
- Default: 5 requests/second
- Adjustable via `RateLimitPerSecond` setting
- Applied to POST /denylist calls only
- GET /logs calls have their own API rate limiting

## Performance Characteristics

### Time Complexity
- Domain normalization: O(n) where n = number of logs
- Deduplication: O(n) HashSet insertion
- HaGeZi classification: O(1) per domain, O(d) total where d = unique domains
- Blocking: O(d) API calls

### Space Complexity
- HaGeZi HashSet: O(45k) = ~45,000 domains
- Blocked domains cache: O(b) where b = previously blocked domains
- In-memory log processing: O(n) for current batch

### Typical Run Time (Assumptions)
- 1000 logs fetched: ~500ms (API latency)
- 500 unique domains: ~100ms (normalization + dedup)
- 50 domains to block @ 5 req/s: ~10 seconds
- **Total: ~10-15 seconds**

## Security Considerations

### API Key Management
```
NEVER commit API keys to code
Use environment variables or Key Vault
Rotate regularly
Implement API key scoping/permissions
```

### Storage Access
```
Use Managed Identity where possible
Implement least privilege access
Enable storage account firewalls
Enable audit logging
```

### Data Privacy
```
Domain names may be sensitive
Implement log retention policies
Encrypt storage at rest
Control access to blocked domains list
```

## Monitoring & Alerting

### Key Metrics

| Metric | Alert Threshold | Action |
|--------|-----------------|--------|
| Pipeline Duration | > 5 minutes | Investigate slow logs |
| Domains Blocked | 0 for 3 runs | Check HaGeZi list/API |
| Failed Blocking | > 10% | Check API rate limits |
| API Errors | > 5 errors | Check NextDNS status |

### Log Levels

```
DEBUG: Detailed domain classifications, API responses
INFO:  Pipeline start/end, statistics, checkpoint updates
WARNING: Retries, timeouts, partial failures
ERROR: API errors, storage failures, fatal issues
```

## Testing Strategy

### Unit Tests (Recommended)

```csharp
[TestClass]
public class BetClassifierTests
{
    [TestMethod]
    public void IsBetDomain_WithValidBetDomain_ReturnsTrue()
    {
        // Arrange
        var classifier = new BetClassifier(...);
        
        // Act
        var result = classifier.IsBetDomain("poker-site.com");
        
        // Assert
        Assert.IsTrue(result);
    }
}
```

### Integration Tests

```csharp
[TestClass]
public class BetBlockerPipelineTests
{
    [TestMethod]
    public async Task ProcessLogs_WithValidLogs_BlocksBetDomains()
    {
        // Use mocked NextDnsClient with sample logs
        // Verify domains are correctly classified and blocked
    }
}
```

### Manual Testing

```bash
# Test configuration
./test-config.sh

# Run locally with docker
docker-compose up --build

# Monitor logs
docker-compose logs -f worker

# Test API calls manually
curl -X GET "https://api.nextdns.io/profiles/{profileId}/logs?limit=1" \
  -H "X-Api-Key: {apiKey}"
```

## Deployment Checklist

- [ ] Environment variables configured (.env)
- [ ] API credentials validated (test-config.sh)
- [ ] Allowlist configured (if needed)
- [ ] Docker/Docker Compose installed
- [ ] Local run successful (docker-compose up)
- [ ] Azure resources created (if deploying to cloud)
- [ ] Storage account tables/containers created
- [ ] Function App configured with correct settings
- [ ] Monitoring/Application Insights configured
- [ ] Backup strategy for blocked domains list
- [ ] Rate limiting verified
- [ ] Initial run monitored for issues

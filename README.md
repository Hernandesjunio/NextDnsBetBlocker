# NextDNS Bet Blocker

A .NET 10 solution for automatically blocking gambling/betting domains on NextDNS using the HaGeZi Gambling blocklist. Runs as a background worker or Azure Function.

## Features

- **Incremental Log Processing**: Fetches logs from NextDNS incrementally using pagination and checkpoints
- **Domain Classification**: Uses HaGeZi Gambling blocklist to classify betting domains
- **Local Allowlist**: Maintain a local list of domains to exempt from blocking
- **Automatic Blocking**: Blocks classified domains via NextDNS denylist API
- **Persistent State**: Tracks processed logs and blocked domains in Azure Table Storage
- **Rate Limiting**: Built-in rate limiting for API calls (configurable, default 5 req/s)
- **Retry Logic**: Exponential backoff for 429 and 5xx errors
- **Flexible Deployment**: Runs locally (Docker), on-premises, or in Azure Functions

## Architecture

### Projects

1. **NextDnsBetBlocker.Core**: Shared interfaces and services
2. **NextDnsBetBlocker.Worker**: Console application for local/on-premises deployment
3. **NextDnsBetBlocker.FunctionApp**: Azure Functions for serverless deployment

### Key Components

- **INextDnsClient**: HTTP client for NextDNS API (logs, denylist)
- **ICheckpointStore**: Tracks last processed log timestamp per profile
- **IBlockedDomainStore**: Records which domains have been blocked
- **IHageziProvider**: Manages HaGeZi Gambling blocklist (cached in Blob Storage)
- **IAllowlistProvider**: Manages local domain allowlist
- **IBetClassifier**: Classifies domains against HaGeZi list
- **IBetBlockerPipeline**: Orchestrates the complete workflow

## Prerequisites

- .NET 10 SDK
- Docker & Docker Compose (for local development)
- NextDNS API Key and Profile ID
- Azure Storage Account (for production deployment)

## Getting Started

### 1. Clone and Setup

```bash
git clone <repository-url>
cd NextDnsBetBlocker
```

### 2. Configure Credentials

Copy the example environment file:

```bash
cp .env.example .env
```

Edit `.env` and add your NextDNS credentials:

```env
NEXTDNS_API_KEY=your_api_key_here
NEXTDNS_PROFILE_ID=your_profile_id_here
```

### 3. Configure Allowlist (Optional)

Edit `src/NextDnsBetBlocker.Worker/allowlist.txt` to add domains that should never be blocked:

```
# Comments start with #
google.com
github.com
your-safe-domain.com
```

## Running Locally with Docker

The easiest way to run locally is using Docker Compose, which automatically sets up Azurite (local Azure Storage emulator):

```bash
# Load environment variables from .env
export $(cat .env | xargs)

# Start Azurite and Worker
docker-compose up --build
```

The container will:
- Start Azurite storage emulator on ports 10000-10002
- Run the Worker service with automatic log processing every 30 minutes
- Refresh HaGeZi list daily
- Store all data locally in `./data` directory

### Logs

View logs in real-time:

```bash
docker-compose logs -f worker
```

### Storage Data

Local development data is stored in:
- `./data/hagezi-gambling-domains.txt` - Cached HaGeZi list
- `./data/blocked-domains.txt` - Local blocked domains record
- `./data/checkpoints.txt` - Processing checkpoints

## Running Locally Without Docker

### 1. Configure appsettings

Edit `src/NextDnsBetBlocker.Worker/appsettings.Development.json`:

```json
{
  "NextDnsApiKey": "your_api_key",
  "NextDnsProfileId": "your_profile_id",
  "UseBlobStorage": false,
  "ProcessingIntervalMinutes": 30
}
```

For local Table Storage, ensure Azurite is running:

```bash
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite:latest
```

### 2. Run the Worker

```bash
cd src/NextDnsBetBlocker.Worker
dotnet run
```

## Deploying to Azure

### Prerequisites

- Azure Storage Account
- Azure Function App (Premium or App Service Plan)
- Service Principal with appropriate permissions

### 1. Create Azure Resources

```bash
# Create resource group
az group create -n MyResourceGroup -l eastus

# Create storage account
az storage account create \
  -n mystorageaccount \
  -g MyResourceGroup \
  -l eastus \
  --sku Standard_LRS

# Create table storage
az storage table create \
  -n BlockedDomains \
  --account-name mystorageaccount

az storage table create \
  -n AgentState \
  --account-name mystorageaccount

# Create Function App
az functionapp create \
  -n MyFunctionApp \
  -g MyResourceGroup \
  --storage-account mystorageaccount \
  --runtime dotnet-isolated \
  --runtime-version 10 \
  --functions-version 4
```

### 2. Configure Function App Settings

```bash
az functionapp config appsettings set \
  -n MyFunctionApp \
  -g MyResourceGroup \
  --settings \
  NextDnsApiKey="your_api_key" \
  NextDnsProfileId="your_profile_id" \
  TableStorageUri="https://mystorageaccount.table.core.windows.net" \
  BlobStorageUri="https://mystorageaccount.blob.core.windows.net" \
  RateLimitPerSecond=5
```

### 3. Deploy Function App

```bash
# Build and publish
dotnet publish src/NextDnsBetBlocker.FunctionApp -c Release -o ./publish

# Deploy to Azure
cd publish
func azure functionapp publish MyFunctionApp --build remote
```

### 4. Upload Allowlist (Optional)

```bash
az storage blob upload \
  --account-name mystorageaccount \
  --container-name function-data \
  --name allowlist.txt \
  --file src/NextDnsBetBlocker.Worker/allowlist.txt
```

## API Configuration

### NextDNS API Requirements

The solution requires:

1. **GET /profiles/:profile/logs** - To fetch query logs
   - Header: `X-Api-Key`
   - Query params: `limit`, `sort`, `cursor`

2. **POST /profiles/:profile/denylist** - To block domains
   - Header: `X-Api-Key`
   - Body: `{ "id": "domain.com", "active": true }`

Get your API Key from NextDNS dashboard.

## Configuration Options

### Worker/Function Settings

All settings can be configured via:
- `appsettings.json` (Worker)
- `local.settings.json` (Function App local)
- Environment variables (Docker/Azure)

| Setting | Default | Description |
|---------|---------|-------------|
| `NextDnsApiKey` | - | NextDNS API Key (required) |
| `NextDnsProfileId` | - | NextDNS Profile ID (required) |
| `ProcessingIntervalMinutes` | 30 | How often to fetch logs |
| `HageziRefreshIntervalHours` | 24 | How often to refresh HaGeZi list |
| `RateLimitPerSecond` | 5 | Rate limit for blocking requests |
| `UseBlobStorage` | false | Use Blob Storage instead of local files |

## Pipeline Workflow

```
1. Load Allowlist
   ↓
2. Get Last Checkpoint (last processed timestamp)
   ↓
3. Fetch Logs from NextDNS (paginated since checkpoint)
   ↓
4. Extract & Normalize Domains (lowercase, trim, deduplicate)
   ↓
5. Load HaGeZi Gambling Blocklist
   ↓
6. For Each Unique Domain:
   - Skip if allowlisted
   - Skip if already blocked
   - Skip if not in HaGeZi list
   - Block via NextDNS API (with rate limiting)
   ↓
7. Update Checkpoint with Latest Timestamp
   ↓
8. Log Statistics
```

## Statistics & Logging

Each run logs detailed statistics:

```
Pipeline Statistics: Duration=00:45, DomainsLogged=1000, UniqueDomains=500, 
DomainsBlocked=50, DomainsSkipped=400, DomainsAllowlisted=5, 
DomainsAlreadyBlocked=45, HageziDomains=45000
```

Use Application Insights in Azure for monitoring:

```bash
# View logs in Azure
az monitor log-analytics query \
  -w /subscriptions/{sub}/resourcegroups/{rg}/providers/microsoft.operationalinsights/workspaces/{workspace} \
  -q "traces | where message contains 'Pipeline Statistics'"
```

## Troubleshooting

### Common Issues

**"401 Unauthorized" from NextDNS**
- Verify API Key is correct
- Ensure API Key has logs and denylist permissions
- Check profile ID matches your NextDNS account

**"No logs returned"**
- Verify profile ID is correct
- Ensure there are recent logs in your NextDNS profile
- Check if last checkpoint is too recent

**"HaGeZi list is empty"**
- Check network connectivity (Azurite/blob storage reachable)
- Verify blob storage path is correct
- Try manual refresh: delete local cache file and restart

**High Memory Usage**
- HaGeZi list can be large (45k+ domains)
- HashSet is memory-efficient but consider if running on low-memory environments
- Reduce refresh frequency if memory is a concern

### Debug Logging

Enable debug logs by setting:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

## Performance Notes

- **Log Fetching**: Paginated with cursor-based pagination (1000 logs per request)
- **Domain Classification**: O(1) lookups using HashSet
- **Rate Limiting**: 5 requests/second default (configurable)
- **Deduplication**: HashSet provides O(1) dedup and lookup
- **Storage**: Checkpoint-based incremental processing avoids reprocessing

## Security Considerations

1. **API Keys**: Store NextDNS API keys in Azure Key Vault, not in code
2. **Storage Keys**: Use Managed Identity or Key Vault for Azure Storage access
3. **Allowlist**: Validate allowlist entries before adding
4. **Logging**: Logs may contain domain names; adjust log retention accordingly

## Contributing

1. Create a feature branch
2. Make changes and test locally with `docker-compose up`
3. Run build: `dotnet build`
4. Submit PR

## License

MIT

## Support

For issues with:
- **NextDNS API**: Check [NextDNS API Docs](https://api.nextdns.io/)
- **HaGeZi Blocklist**: See [HaGeZi GitHub](https://github.com/hagezi/dns-blocklists)
- **Azure Functions**: Check [Azure Functions Docs](https://docs.microsoft.com/azure/azure-functions/)

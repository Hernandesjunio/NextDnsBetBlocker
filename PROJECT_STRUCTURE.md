# Project Structure

## Overview

```
NextDnsBetBlocker/
├── README.md                          # Main documentation
├── AZURE_DEPLOYMENT.md                # Azure deployment guide
├── TECHNICAL_ARCHITECTURE.md          # Technical details
├── TESTING_EXAMPLES.md                # Test examples
├── NextDnsBetBlocker.sln              # Solution file
├── docker-compose.yml                 # Docker composition for local dev
├── .env.example                       # Example environment variables
├── .gitignore                         # Git ignore rules
├── setup.sh                           # Setup script
├── test-config.sh                     # Configuration test script
├── data/                              # Local development data (git ignored)
│   ├── hagezi-gambling-domains.txt
│   ├── blocked-domains.txt
│   └── checkpoints.txt
│
├── src/
│   │
│   ├── NextDnsBetBlocker.Core/        # Shared library
│   │   ├── NextDnsBetBlocker.Core.csproj
│   │   ├── Models/
│   │   │   └── Models.cs              # Data models and DTOs
│   │   ├── Interfaces/
│   │   │   └── Interfaces.cs          # All service interfaces
│   │   └── Services/
│   │       ├── NextDnsClient.cs       # HTTP client for NextDNS API
│   │       ├── CheckpointStore.cs     # Checkpoint persistence (Azure Tables)
│   │       ├── BlockedDomainStore.cs  # Blocked domains storage
│   │       ├── HageziProvider.cs      # HaGeZi blocklist management
│   │       ├── AllowlistProvider.cs   # Local allowlist management
│   │       ├── BetClassifier.cs       # Domain classification
│   │       └── BetBlockerPipeline.cs  # Main pipeline orchestration
│   │
│   ├── NextDnsBetBlocker.Worker/      # Console application
│   │   ├── NextDnsBetBlocker.Worker.csproj
│   │   ├── Program.cs                 # DI setup and host builder
│   │   ├── WorkerSettings.cs          # Configuration model
│   │   ├── WorkerService.cs           # Background service with timers
│   │   ├── LocalBlockedDomainStore.cs # Local file implementation
│   │   ├── LocalCheckpointStore.cs    # Local file implementation
│   │   ├── LocalBlobClient.cs         # Local file blob emulation
│   │   ├── Dockerfile                 # Docker image for worker
│   │   ├── appsettings.json           # Production settings
│   │   ├── appsettings.Development.json # Dev settings (Azurite)
│   │   └── allowlist.txt              # Local domain allowlist
│   │
│   └── NextDnsBetBlocker.FunctionApp/ # Azure Functions
│       ├── NextDnsBetBlocker.FunctionApp.csproj
│       ├── Startup.cs                 # DI setup for Azure Functions
│       ├── FunctionAppSettings.cs     # Configuration model
│       ├── ProcessLogsFunction.cs     # Timer function (30 min)
│       ├── UpdateHageziFunction.cs    # Timer function (daily)
│       ├── host.json                  # Function runtime config
│       ├── local.settings.json        # Local settings (git ignored)
│       └── extensions.json            # Extension bundle config
│
└── .github/
    └── workflows/
        └── deploy.yml                 # (Optional) CI/CD pipeline
```

## File Descriptions

### Core Project (`NextDnsBetBlocker.Core`)

**Models/Models.cs**
- `NextDnsLogsResponse` - API response structure
- `LogEntry` - Individual log entry
- `DenylistBlockRequest` - Domain blocking request
- `BlockerRunStatistics` - Pipeline execution metrics
- Entity models for storage

**Interfaces/Interfaces.cs**
- `INextDnsClient` - NextDNS API operations
- `ICheckpointStore` - Checkpoint management
- `IBlockedDomainStore` - Blocked domain registry
- `IHageziProvider` - HaGeZi blocklist access
- `IAllowlistProvider` - Allowlist management
- `IBetClassifier` - Domain classification
- `IBetBlockerPipeline` - Pipeline orchestration

**Services**
- `NextDnsClient` - Implements INextDnsClient with retry/rate limiting
- `CheckpointStore` - Manages checkpoints in Azure Tables
- `BlockedDomainStore` - Manages blocked domains in Azure Tables
- `HageziProvider` - Fetches/caches HaGeZi blocklist from GitHub/Blob
- `AllowlistProvider` - Loads local allowlist from file
- `BetClassifier` - Classifies domains using HaGeZi list
- `BetBlockerPipeline` - Orchestrates complete workflow

### Worker Project (`NextDnsBetBlocker.Worker`)

**Program.cs**
- Hosts application builder
- Sets up dependency injection
- Configures logging
- Loads configuration from appsettings + environment

**WorkerService.cs**
- Implements `IHostedService`
- Runs two periodic timers:
  - Every 30 minutes: Process logs
  - Every 24 hours: Update HaGeZi
- Handles graceful shutdown

**Local Storage Implementations**
- `LocalBlockedDomainStore` - File-based blocked domains storage
- `LocalCheckpointStore` - File-based checkpoint storage
- `LocalBlobClient` - Local file system blob emulation

**Dockerfile**
- Multi-stage build
- Final image: .NET 10 runtime
- Exposes port for logging

**appsettings files**
- `appsettings.json` - Production defaults
- `appsettings.Development.json` - Azurite endpoints

**allowlist.txt**
- One domain per line
- Lines starting with `#` are comments
- Domains are normalized automatically

### Function App Project (`NextDnsBetBlocker.FunctionApp`)

**Startup.cs**
- Implements `FunctionsStartup`
- Configures DI for Azure Functions
- Sets up HTTP client, storage clients, and services

**ProcessLogsFunction.cs**
- Timer trigger: `0 */30 * * * *` (every 30 minutes)
- Calls `IBetBlockerPipeline.ProcessLogsAsync()`
- Logs statistics to Application Insights

**UpdateHageziFunction.cs**
- Timer trigger: `0 0 0 * * *` (daily at 00:00 UTC)
- Calls `IBetBlockerPipeline.UpdateHageziAsync()`

**host.json**
- Function runtime configuration
- Extension bundle settings
- Function timeout: 5 minutes

### Configuration Files

**.env.example**
- Template for environment variables
- Copy to `.env` and fill in actual values

**.env (git ignored)**
- Local environment variables
- NextDNS API key and profile ID
- Loaded by docker-compose

**docker-compose.yml**
- Service: azurite (storage emulator)
- Service: worker (NextDNS blocker)
- Volumes for persistent data
- Health checks

**setup.sh**
- Interactive setup script
- Creates .env from template
- Checks prerequisites
- Creates necessary directories

**test-config.sh**
- Tests NextDNS API connectivity
- Validates credentials
- Shows sample API response

## Data Flow

### Startup
```
1. Load configuration (appsettings + env vars)
2. Setup DI container
3. Initialize logging
4. Start WorkerService / Azure Functions runtime
```

### Processing Cycle (Every 30 minutes)
```
1. ProcessLogsFunction triggered
2. IBetBlockerPipeline.ProcessLogsAsync() called
3. Load allowlist from disk/blob
4. Get last checkpoint from storage
5. Fetch NextDNS logs (paginated from checkpoint)
6. Normalize and deduplicate domains
7. Load HaGeZi list from cache/blob
8. Classify each domain
9. Block unallowlisted, unblocked, classified domains
10. Update checkpoint
11. Log statistics
```

### HaGeZi Refresh (Daily)
```
1. UpdateHageziFunction triggered
2. IBetBlockerPipeline.UpdateHageziAsync() called
3. Download latest HaGeZi list from GitHub
4. Parse adblock format
5. Save to local cache + Blob Storage
6. Update in-memory HashSet
```

## Dependency Tree

```
BetBlockerPipeline
├── INextDnsClient
│   └── HttpClient (via HttpClientFactory)
├── ICheckpointStore
│   └── TableClient (Azure Tables or local)
├── IBlockedDomainStore
│   └── TableClient (Azure Tables or local)
├── IHageziProvider
│   └── BlobContainerClient (Blob Storage or local)
├── IAllowlistProvider
│   └── File system
└── IBetClassifier
    └── IHageziProvider
```

## Configuration Hierarchy

1. **appsettings.json** (base)
2. **appsettings.{Environment}.json** (environment-specific)
3. **Environment variables** (highest priority)
4. **Secrets** (Azure Key Vault in production)

## Storage Paths

### Development (Local)
```
./data/
├── hagezi-gambling-domains.txt
├── blocked-domains.txt
└── checkpoints.txt
```

### Production (Azure)
```
Table Storage:
- AgentState table
- BlockedDomains table

Blob Storage:
- blocklists container
  └── hagezi-gambling-domains.txt
```

## Key Design Decisions

1. **HashSet for HaGeZi**: Fast O(1) lookups, moderate memory usage
2. **Table Storage for State**: Indexed by profile ID, easy scaling
3. **Checkpoint-based Processing**: Avoids reprocessing logs
4. **Rate Limiting in Pipeline**: Prevents API throttling
5. **Local Implementations for Dev**: Azurite not required
6. **Separated Concerns**: Each interface handles one responsibility
7. **Async/Await**: Non-blocking I/O throughout
8. **Structured Logging**: ILogger for observability

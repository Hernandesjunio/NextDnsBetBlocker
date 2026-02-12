# Executive Summary

## Project Overview

**NextDNS Bet Blocker** is a production-ready .NET 10 solution that automatically identifies and blocks gambling/betting domains on NextDNS. It operates as a background service that:

1. Fetches DNS query logs from NextDNS API every 30 minutes
2. Classifies domains against the HaGeZi Gambling blocklist
3. Automatically blocks new betting domains via NextDNS denylist
4. Maintains local and allowlisted domains that should never be blocked
5. Tracks all state persistently using Azure Table Storage
6. Refreshes the HaGeZi blocklist daily

## Key Features

✅ **Automated Domain Detection** - Identifies betting/gambling domains from DNS logs
✅ **Intelligent Classification** - Uses HaGeZi community-maintained gambling blocklist
✅ **Production-Ready** - Retry logic, rate limiting, error handling, and structured logging
✅ **Flexible Deployment** - Works locally (Docker), on-premises, or in Azure
✅ **Persistent State** - Checkpoint-based processing prevents duplicate blocking
✅ **Local Allowlist** - Protect safe domains from being blocked
✅ **Scalable** - Works with multiple NextDNS profiles
✅ **Cost-Effective** - $5-15/month on Azure Consumption plan

## Architecture

```
NextDNS API ──→ [Fetch Logs] ──→ [Normalize] ──→ [Classify] ──→ [Block] ──→ NextDNS Denylist
                                                      ↓
                                            HaGeZi Blocklist
                                                      
State persisted in:
  • Azure Table Storage (checkpoints, blocked domains)
  • Azure Blob Storage (HaGeZi cache)
```

## Deployment Options

### Option 1: Local/Docker (Recommended for Testing)
```bash
docker-compose up --build
```
- Runs locally with Azurite storage emulator
- No Azure account required
- Perfect for development and validation
- Takes 5 minutes to set up

### Option 2: Azure Cloud (Recommended for Production)
```bash
# Create resources (10 minutes)
az group create --name nextdns-rg --location eastus
az storage account create --name mystorageaccount --resource-group nextdns-rg
az functionapp create --name MyFunctionApp ...

# Deploy (5 minutes)
func azure functionapp publish MyFunctionApp --build remote
```
- Fully serverless (pay only when it runs)
- Integrated monitoring with Application Insights
- ~$5-15/month for typical usage
- Enterprise-grade reliability

## Configuration

Three simple steps to get started:

**1. Get NextDNS Credentials**
```
Visit: https://nextdns.io/settings/api
Copy: API Key and Profile ID
```

**2. Configure Environment**
```bash
cp .env.example .env
# Edit .env with your credentials
```

**3. Configure Allowlist (Optional)**
```bash
# Add safe domains to allowlist.txt
echo "github.com" >> src/NextDnsBetBlocker.Worker/allowlist.txt
```

## How It Works

### Processing Pipeline

1. **Load State**: Retrieve last processed timestamp (or start from beginning)
2. **Fetch Logs**: Get DNS query logs from NextDNS API with pagination
3. **Normalize**: Convert domains to lowercase, trim whitespace, deduplicate
4. **Classify**: Check each domain against HaGeZi gambling blocklist
5. **Block**: For unallowlisted, unblocked domains → send to NextDNS denylist
6. **Persist**: Save checkpoint and blocked domain records

### Example Execution

```
Time: 2024-01-15 14:30:00
─────────────────────────────────
Domains logged: 1,000
Unique domains: 500
HaGeZi matched: 50 bet/gambling sites
Allowlisted: 5 (skipped)
Already blocked: 15 (idempotent)
Newly blocked: 30 ✓
Skipped: 455 (not gambling)
Duration: 12 seconds
─────────────────────────────────
Next run: 15:00:00
```

## Statistics & Monitoring

Each run produces detailed metrics:

```json
{
  "DomainsLogged": 1000,
  "UniqueDomains": 500,
  "DomainsBlocked": 30,
  "DomainsSkipped": 455,
  "DomainsAllowlisted": 5,
  "DomainsAlreadyBlocked": 15,
  "HageziTotalDomains": 45000,
  "Duration": "00:00:12"
}
```

View real-time analytics in Azure Application Insights or container logs.

## Data Privacy & Security

✅ **API Keys Protected** - Store in Key Vault, never in code
✅ **Logs Secure** - All data encrypted at rest in Azure
✅ **Idempotent** - Safe to retry without duplicate blocks
✅ **Local Allowlist** - Complete control over what's blocked
✅ **Audit Trail** - All blocking events logged

## Cost Analysis

### Typical Monthly Cost (Both Deployments)

| Component | Cost | Notes |
|-----------|------|-------|
| Compute | $0 - $3 | Function App executions (1440/month) |
| Storage | $0.05 - $0.10 | ~50KB of state data |
| API Calls | $0 | Included in NextDNS subscription |
| Monitoring | $0 - $2.50 | Application Insights (optional) |
| **Total** | **~$5-15/month** | May vary by usage |

## Project Structure

```
src/
├── NextDnsBetBlocker.Core/       # Shared library (interfaces & services)
├── NextDnsBetBlocker.Worker/     # Console application
└── NextDnsBetBlocker.FunctionApp/# Azure Functions
```

**Key Interfaces:**
- `INextDnsClient` - NextDNS API communication
- `IBetBlockerPipeline` - Main orchestration logic
- `IHageziProvider` - Gambling blocklist management
- `ICheckpointStore` - State persistence
- `IAllowlistProvider` - Allowlist management

## Documentation

| Document | Purpose |
|----------|---------|
| **README.md** | Quick start guide and setup instructions |
| **AZURE_DEPLOYMENT.md** | Step-by-step Azure deployment |
| **TECHNICAL_ARCHITECTURE.md** | Detailed system design |
| **PROJECT_STRUCTURE.md** | File organization and dependencies |
| **COMMANDS_REFERENCE.md** | Useful CLI commands |
| **TESTING_EXAMPLES.md** | Unit test templates |

## Quick Start

```bash
# 1. Setup (2 minutes)
chmod +x setup.sh
./setup.sh

# 2. Configure credentials
nano .env

# 3. Start (30 seconds)
docker-compose up --build

# 4. Monitor
docker-compose logs -f worker
```

That's it! The service will:
- ✓ Fetch logs every 30 minutes
- ✓ Block new gambling domains automatically
- ✓ Refresh HaGeZi list daily
- ✓ Maintain idempotency (no duplicate blocks)

## Use Cases

### 1. Parental Controls
Block gambling sites for children on the family network

### 2. Corporate Policy Enforcement
Prevent access to betting sites on corporate networks

### 3. Internet Filtering
Part of a comprehensive DNS filtering solution

### 4. Organizational Compliance
Meet regulatory requirements to block gambling content

## Advantages Over Alternatives

| Feature | NextDNS Bet Blocker | Manual Lists | Other Tools |
|---------|-------------------|--------------|-------------|
| Automated | ✅ | ❌ | ⚠️ |
| Community-Maintained | ✅ | ⚠️ | ⚠️ |
| Self-Hosted Option | ✅ | ✅ | ❌ |
| Easy Setup | ✅ | ❌ | ⚠️ |
| Cost | $5-15/mo | Free | Varies |
| Real-Time Processing | ✅ | ❌ | ⚠️ |

## Roadmap

**Current (v1.0):**
- Single profile support
- HaGeZi gambling blocklist
- Docker + Azure Functions

**Planned (v2.0):**
- Multiple profiles
- Custom blocklists
- Web UI dashboard
- Webhook notifications
- Alternative blocklist sources

## Support

**Issue?** Check:
1. README.md - Most common questions answered
2. TECHNICAL_ARCHITECTURE.md - How the system works
3. COMMANDS_REFERENCE.md - Useful diagnostics
4. Run `test-config.sh` - Validate credentials

**Need Help?**
- Review Docker logs: `docker-compose logs worker`
- Validate NextDNS setup: `./test-config.sh`
- Check Application Insights (if on Azure)

## License

MIT License - Free for personal and commercial use

## Requirements

**Minimum:**
- .NET 10 SDK (or just runtime for deployed version)
- Docker & Docker Compose (for local dev)
- NextDNS account with API access

**For Azure:**
- Azure subscription (free tier eligible)
- Azure CLI installed

## Getting Help

1. **Setup Issues** → See README.md
2. **Configuration** → Run `test-config.sh`
3. **Azure Deployment** → See AZURE_DEPLOYMENT.md
4. **Architecture Questions** → See TECHNICAL_ARCHITECTURE.md
5. **Command Reference** → See COMMANDS_REFERENCE.md

## Summary

NextDNS Bet Blocker is a **lightweight, production-ready solution** that automatically blocks gambling domains using a trusted community-maintained blocklist. It can be deployed in **5 minutes locally** or **15 minutes on Azure**, costs **less than $15/month**, and requires **minimal configuration**.

Perfect for:
- 👨‍👩‍👧‍👦 Parents wanting to protect children
- 🏢 Organizations enforcing policies
- 🔒 Security-conscious individuals
- 🌐 Anyone using NextDNS

**Start now:** `./setup.sh && docker-compose up`

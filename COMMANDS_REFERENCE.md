# Useful Commands & Troubleshooting

## Development & Testing

### Setup & Initialization

```bash
# Clone repository
git clone <repo-url>
cd NextDnsBetBlocker

# Run setup script (interactive)
chmod +x setup.sh
./setup.sh

# Or manual setup
cp .env.example .env
# Edit .env with your credentials
nano .env
```

### Local Docker Development

```bash
# Start services (builds and starts)
docker-compose up --build

# Start in background
docker-compose up -d --build

# View logs
docker-compose logs -f worker

# View specific service logs
docker-compose logs -f azurite
docker-compose logs -f worker

# Stop services
docker-compose down

# Remove volumes (clean slate)
docker-compose down -v

# Restart services
docker-compose restart

# Rebuild without cache
docker-compose build --no-cache
```

### Testing Configuration

```bash
# Test NextDNS API connectivity
chmod +x test-config.sh
./test-config.sh

# Manual API test
NEXTDNS_API_KEY="your_key"
NEXTDNS_PROFILE_ID="your_profile"

curl -X GET "https://api.nextdns.io/profiles/$NEXTDNS_PROFILE_ID/logs?limit=5" \
  -H "X-Api-Key: $NEXTDNS_API_KEY"

# Test denylist endpoint
curl -X POST "https://api.nextdns.io/profiles/$NEXTDNS_PROFILE_ID/denylist" \
  -H "X-Api-Key: $NEXTDNS_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"id":"test.example.com","active":true}'
```

### Local Storage Inspection

```bash
# Check blocked domains
cat data/blocked-domains.txt

# Check checkpoints
cat data/checkpoints.txt

# View HaGeZi cache size
wc -l data/hagezi-gambling-domains.txt

# Clear local data
rm -rf data/
mkdir data

# View allowlist
cat src/NextDnsBetBlocker.Worker/allowlist.txt
```

### Building & Running Without Docker

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build -c Release

# Run tests (once created)
dotnet test

# Run worker directly
cd src/NextDnsBetBlocker.Worker
dotnet run --configuration Release

# Publish for deployment
dotnet publish -c Release -o ./publish
```

## Azure Deployment

### Resource Management

```bash
# List all resources
az resource list --resource-group MyResourceGroup

# Create resource group
az group create \
  --name nextdns-rg \
  --location eastus

# Delete resource group
az group delete \
  --name nextdns-rg \
  --yes

# Show resource details
az functionapp show \
  --resource-group nextdns-rg \
  --name my-function-app
```

### Storage Management

```bash
# Get storage connection string
az storage account show-connection-string \
  --resource-group nextdns-rg \
  --name mystorageaccount

# List tables
az storage table list --account-name mystorageaccount

# Create table
az storage table create \
  --name MyTable \
  --account-name mystorageaccount

# List blobs
az storage blob list \
  --account-name mystorageaccount \
  --container-name blocklists

# Upload blob
az storage blob upload \
  --account-name mystorageaccount \
  --container-name blocklists \
  --name allowlist.txt \
  --file ./allowlist.txt

# Download blob
az storage blob download \
  --account-name mystorageaccount \
  --container-name blocklists \
  --name hagezi-gambling-domains.txt \
  --file ./hagezi-cache.txt
```

### Function App Management

```bash
# Deploy Function App
func azure functionapp publish MyFunctionApp --build remote

# Show function app settings
az functionapp config appsettings list \
  --resource-group nextdns-rg \
  --name MyFunctionApp

# Update settings
az functionapp config appsettings set \
  --resource-group nextdns-rg \
  --name MyFunctionApp \
  --settings \
  NextDnsApiKey="new_key" \
  RateLimitPerSecond=10

# Restart function app
az functionapp restart \
  --resource-group nextdns-rg \
  --name MyFunctionApp

# View function app logs
az functionapp log show \
  --resource-group nextdns-rg \
  --name MyFunctionApp
```

### Application Insights Queries

```bash
# View all traces
az monitor log-analytics query \
  --workspace /subscriptions/{sub}/resourcegroups/{rg}/providers/microsoft.operationalinsights/workspaces/{workspace} \
  --analytics-query "traces | order by timestamp desc | limit 50"

# Find pipeline statistics
az monitor log-analytics query \
  --workspace {workspace} \
  --analytics-query "traces | where message contains 'Pipeline Statistics' | project timestamp, message"

# Find errors
az monitor log-analytics query \
  --workspace {workspace} \
  --analytics-query "traces | where severityLevel >= 2 | order by timestamp desc"

# Monitor domain blocking
az monitor log-analytics query \
  --workspace {workspace} \
  --analytics-query "traces | where message contains 'Successfully blocked' | summarize count()"
```

## Monitoring & Diagnostics

### Docker Diagnostics

```bash
# Check container status
docker ps

# Check container logs
docker logs <container-id>

# Get container stats
docker stats

# Access container shell
docker exec -it <container-id> /bin/sh

# Get container IP
docker inspect <container-id> | grep IPAddress
```

### Azurite Diagnostics

```bash
# Test Azurite blob storage
curl -X GET http://127.0.0.1:10000/devstoreaccount1?comp=list

# Test Azurite table storage
curl -X GET "http://127.0.0.1:10002/devstoreaccount1/Tables" \
  -H "Authorization: Bearer dev"

# Connect to Azurite with Azure Storage Explorer
# Account: devstoreaccount1
# Key: Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==
# Blob Service Endpoint: http://127.0.0.1:10000/devstoreaccount1
# Table Service Endpoint: http://127.0.0.1:10002/devstoreaccount1
```

### Performance Profiling

```bash
# Monitor memory usage
docker stats --no-stream worker

# Check process resources
ps aux | grep dotnet

# Profile with dotnet-trace
dotnet trace collect -p <pid> --providers Microsoft-Windows-DotNETRuntime

# View traces
dotnet trace report trace.nettrace
```

## File Management

### Allowlist Management

```bash
# Add domain to allowlist
echo "safe-domain.com" >> src/NextDnsBetBlocker.Worker/allowlist.txt

# Remove domain from allowlist
sed -i '/safe-domain.com/d' src/NextDnsBetBlocker.Worker/allowlist.txt

# Sort allowlist
sort -u src/NextDnsBetBlocker.Worker/allowlist.txt -o src/NextDnsBetBlocker.Worker/allowlist.txt

# Check allowlist for duplicates
sort src/NextDnsBetBlocker.Worker/allowlist.txt | uniq -d

# Import allowlist from another source
cat external-allowlist.txt >> src/NextDnsBetBlocker.Worker/allowlist.txt
```

### Log Analysis

```bash
# Check for errors in Docker logs
docker-compose logs worker | grep -i error

# Count blocking events
docker-compose logs worker | grep "Successfully blocked" | wc -l

# View pipeline statistics
docker-compose logs worker | grep "Pipeline Statistics"

# Export logs to file
docker-compose logs > application.log

# Search across all components
docker-compose logs | grep "pattern"
```

## Troubleshooting Commands

### Connection Issues

```bash
# Test connectivity to NextDNS
ping api.nextdns.io
telnet api.nextdns.io 443

# Test DNS resolution
nslookup api.nextdns.io

# Check proxy settings (if behind corporate proxy)
curl -x <proxy:port> https://api.nextdns.io/profiles/{id}/logs

# Test Azurite connectivity from Docker
docker run --network host curlimages/curl:latest \
  curl -X GET http://127.0.0.1:10002/
```

### Configuration Issues

```bash
# Validate JSON configuration
jq . src/NextDnsBetBlocker.Worker/appsettings.json

# Check environment variables in container
docker exec worker env | grep NextDns

# Verify file permissions
ls -la data/
ls -la src/NextDnsBetBlocker.Worker/

# Check if files are accessible
test -r data/allowlist.txt && echo "readable" || echo "not readable"
```

### Performance Issues

```bash
# Monitor CPU usage
top -p $(pgrep -f dotnet)

# Check disk space
df -h

# Monitor network traffic
netstat -an | grep ESTABLISHED | wc -l

# Check file descriptor limits
ulimit -n

# List all open connections
lsof -p $(pgrep -f dotnet)
```

## Database Debugging

### Azure Tables

```bash
# Query table entries (via Azure CLI)
az storage entity query \
  --table-name BlockedDomains \
  --account-name mystorageaccount

# Specific partition
az storage entity query \
  --table-name BlockedDomains \
  --account-name mystorageaccount \
  --filter "PartitionKey eq 'my-profile-id'"

# Delete entity
az storage entity delete \
  --table-name BlockedDomains \
  --account-name mystorageaccount \
  --partition-key 'my-profile-id' \
  --row-key 'domain.com'
```

### Local File Storage

```bash
# List blocked domains
awk -F'|' '{print $2}' data/blocked-domains.txt | sort | uniq | wc -l

# Find when a domain was blocked
grep "domain-name.com" data/blocked-domains.txt

# Get most recently blocked domains
tail -10 data/blocked-domains.txt

# Backup data
cp -r data/ data-backup-$(date +%s)/
```

## Cleanup Operations

```bash
# Remove old Azure resources
az group delete --name old-resource-group --yes

# Clean docker images
docker image prune -a

# Clean docker volumes
docker volume prune

# Reset local development environment
docker-compose down -v
rm -rf data/
rm .env
cp .env.example .env

# Archive old logs
tar -czf logs-$(date +%Y%m%d).tar.gz *.log

# Delete old backups
find . -name "data-backup-*" -type d -mtime +30 -exec rm -rf {} \;
```

## Useful One-Liners

```bash
# Get count of unique domains in HaGeZi
wc -l data/hagezi-gambling-domains.txt

# Get count of blocked domains
awk -F'|' '{print $2}' data/blocked-domains.txt | sort | uniq | wc -l

# See last 10 blocked domains
tail -10 data/blocked-domains.txt | awk -F'|' '{print $2}'

# Monitor for new blockings in real-time
tail -f data/blocked-domains.txt | while read line; do echo "BLOCKED: $(echo $line | awk -F'|' '{print $2}')"; done

# Count API calls in logs
docker-compose logs worker | grep "AddToDenylistAsync\|GetLogsAsync" | wc -l

# Calculate average blocking rate
docker-compose logs worker | grep "DomainsBlocked" | awk -F'[,=]' '{sum+=$9; count++} END {print "Avg domains blocked:", sum/count}'
```

## Emergency Procedures

### If Worker Crashes

```bash
# Restart service
docker-compose restart worker

# Check what went wrong
docker-compose logs worker --tail 100

# Full restart
docker-compose down
docker-compose up -d

# Manual restart outside Docker
cd src/NextDnsBetBlocker.Worker
dotnet run --configuration Release
```

### If Storage is Corrupted

```bash
# Backup current data
cp -r data/ data-corrupted-$(date +%s)/

# Reset checkpoints (forces reprocessing from beginning)
rm data/checkpoints.txt

# Keep blocked domains, just clear checkpoints
# Next run will reprocess older logs but maintain block list
```

### If API Key Expires

```bash
# Update in .env
nano .env

# Reload in Docker
docker-compose down
docker-compose up -d

# Or update in Azure
az functionapp config appsettings set \
  --resource-group nextdns-rg \
  --name MyFunctionApp \
  --settings NextDnsApiKey="new_key"
```

## Additional Resources

- NextDNS API Docs: https://api.nextdns.io/
- HaGeZi Project: https://github.com/hagezi/dns-blocklists
- Azure CLI Reference: https://docs.microsoft.com/cli/azure/
- .NET 10 Documentation: https://learn.microsoft.com/dotnet/
- Docker Documentation: https://docs.docker.com/

# Azure Deployment Guide

## Prerequisites

- Azure CLI installed and configured
- Azure subscription with appropriate permissions
- .NET 10 SDK installed locally
- Azure Functions Core Tools (optional, for local testing)

## Step-by-Step Deployment

### 1. Create Resource Group

```bash
# Set variables
RESOURCE_GROUP="nextdns-bet-blocker-rg"
LOCATION="eastus"
STORAGE_ACCOUNT="nextdnsstorage$(date +%s)"
FUNCTION_APP="nextdns-bet-blocker-app"

# Create resource group
az group create \
  --name $RESOURCE_GROUP \
  --location $LOCATION
```

### 2. Create Azure Storage Account

```bash
# Create storage account
az storage account create \
  --name $STORAGE_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --sku Standard_LRS \
  --kind StorageV2

# Create required tables
az storage table create \
  --name BlockedDomains \
  --account-name $STORAGE_ACCOUNT \
  --account-key $(az storage account keys list --account-name $STORAGE_ACCOUNT --query "[0].value" -o tsv)

az storage table create \
  --name AgentState \
  --account-name $STORAGE_ACCOUNT \
  --account-key $(az storage account keys list --account-name $STORAGE_ACCOUNT --query "[0].value" -o tsv)

# Create blob container for HaGeZi cache
az storage container create \
  --name blocklists \
  --account-name $STORAGE_ACCOUNT \
  --account-key $(az storage account keys list --account-name $STORAGE_ACCOUNT --query "[0].value" -o tsv)
```

### 3. Create Function App

```bash
# Create App Service Plan (Premium tier recommended for timer triggers)
az appservice plan create \
  --name nextdns-plan \
  --resource-group $RESOURCE_GROUP \
  --is-linux \
  --sku P1V2

# Create Function App
az functionapp create \
  --resource-group $RESOURCE_GROUP \
  --consumption-plan-location $LOCATION \
  --runtime dotnet-isolated \
  --runtime-version 10 \
  --functions-version 4 \
  --name $FUNCTION_APP \
  --storage-account $STORAGE_ACCOUNT \
  --os-type Linux
```

### 4. Configure Function App Settings

```bash
# Get storage account connection info
STORAGE_KEY=$(az storage account keys list --account-name $STORAGE_ACCOUNT --query "[0].value" -o tsv)
TABLE_URI="https://${STORAGE_ACCOUNT}.table.core.windows.net"
BLOB_URI="https://${STORAGE_ACCOUNT}.blob.core.windows.net"

# Set application settings
az functionapp config appsettings set \
  --name $FUNCTION_APP \
  --resource-group $RESOURCE_GROUP \
  --settings \
  "NextDnsApiKey=YOUR_NEXTDNS_API_KEY_HERE" \
  "NextDnsProfileId=YOUR_NEXTDNS_PROFILE_ID_HERE" \
  "TableStorageUri=${TABLE_URI}" \
  "BlobStorageUri=${BLOB_URI}" \
  "StorageAccountName=${STORAGE_ACCOUNT}" \
  "StorageAccountKey=${STORAGE_KEY}" \
  "RateLimitPerSecond=5" \
  "FUNCTIONS_EXTENSION_VERSION=~4"
```

### 5. Upload Allowlist (Optional)

Create a blob container and upload allowlist:

```bash
# Upload allowlist to blob storage
az storage blob upload \
  --account-name $STORAGE_ACCOUNT \
  --account-key $STORAGE_KEY \
  --container-name blocklists \
  --name allowlist.txt \
  --file src/NextDnsBetBlocker.Worker/allowlist.txt
```

Update the Function App code to read from blob instead of local filesystem.

### 6. Deploy Function App

Option A: Using Azure CLI

```bash
# Build and publish
dotnet publish src/NextDnsBetBlocker.FunctionApp \
  -c Release \
  -o ./publish-func

# Deploy
func azure functionapp publish $FUNCTION_APP \
  --build remote
```

Option B: Using GitHub Actions (Recommended for CI/CD)

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy to Azure Functions

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      
      - name: Build
        run: dotnet build -c Release
      
      - name: Publish
        run: dotnet publish src/NextDnsBetBlocker.FunctionApp -c Release -o ./publish
      
      - name: Deploy
        uses: Azure/functions-action@v1
        with:
          app-name: ${{ secrets.FUNCTION_APP_NAME }}
          package: './publish'
          publish-profile: ${{ secrets.AZURE_FUNCTIONAPP_PUBLISH_PROFILE }}
```

### 7. Verify Deployment

```bash
# Check function app status
az functionapp show \
  --resource-group $RESOURCE_GROUP \
  --name $FUNCTION_APP

# View recent logs
az functionapp log show \
  --resource-group $RESOURCE_GROUP \
  --name $FUNCTION_APP \
  --provider http
```

## Monitoring

### Application Insights

The Function App automatically integrates with Application Insights. View logs:

```bash
# Get instrumentation key
INSTRUMENTATION_KEY=$(az functionapp config appsettings list \
  --resource-group $RESOURCE_GROUP \
  --name $FUNCTION_APP \
  --query "[?name=='APPINSIGHTS_INSTRUMENTATIONKEY'].value" -o tsv)

echo "Application Insights Key: $INSTRUMENTATION_KEY"
```

View in Azure Portal → Function App → Application Insights

### Custom Queries

```kusto
// View all pipeline runs
traces
| where message contains "Pipeline Statistics"
| project TimeGenerated, message
| order by TimeGenerated desc

// View errors
traces
| where severityLevel >= 2
| project TimeGenerated, message
| order by TimeGenerated desc

// Domain blocking statistics
customMetrics
| where name == "DomainsBlocked"
| summarize Avg=avg(value), Max=max(value), Count=count() by bin(timestamp, 1h)
```

## Cost Estimation

Monthly costs (estimate):

| Component | Tier | Cost |
|-----------|------|------|
| Function App | Consumption | $0.20/million executions + compute |
| Storage Account | Standard LRS | ~$0.025/GB stored |
| Application Insights | Pay-as-you-go | ~$2.50 per GB ingested |

For light usage (30-min intervals, ~1440 executions/month):
- **Estimated monthly cost: $5-15**

## Troubleshooting

### Function Not Triggering

1. Check timer trigger syntax in code
2. Verify function app settings are correct
3. View logs in Application Insights

### API Errors

```kusto
// Find 401/429 errors
traces
| where message contains "Failed to"
| project TimeGenerated, message
```

### Storage Connection Issues

```bash
# Test storage connectivity
az storage account show-connection-string \
  --resource-group $RESOURCE_GROUP \
  --name $STORAGE_ACCOUNT
```

## Cleanup

```bash
# Delete entire resource group (removes all resources)
az group delete \
  --name $RESOURCE_GROUP \
  --yes --no-wait
```

## Next Steps

1. Set up Application Insights alerts for errors
2. Configure daily HaGeZi refresh (already in code, runs daily at midnight UTC)
3. Adjust rate limiting based on NextDNS API limits
4. Monitor initial runs and adjust processing interval as needed
5. Set up backup/export of blocked domains periodically

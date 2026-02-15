# 🚀 DEPLOYMENT AUTOMATION GUIDE - Etapas 8-10

> Complete automation for pushing to ACR, deploying to ACI, and setting up scheduler

## 📋 Overview

This guide automates the final three deployment steps:
- **Step 8**: Push Docker image to Azure Container Registry (ACR)
- **Step 9**: Deploy and test in Azure Container Instances (ACI)
- **Step 10**: Deploy weekly scheduler using Logic Apps (Bicep)

---

## 🛠️ Prerequisites

### Required Tools

```powershell
# Check versions
docker --version          # v27.0.3 or newer
az --version              # Azure CLI 2.50+ 
bicep --version           # Latest

# Install if missing
choco install azure-cli   # Windows Chocolatey
choco install docker-desktop

# Bicep (via Azure CLI)
az bicep install
```

### Azure Preparation

```powershell
# Login to Azure
az login

# Set default subscription (if needed)
az account set --subscription "Your Subscription Name"

# Verify current context
az account show --query "{User: user.name, Subscription: name}" -o table
```

### Prerequisites Validation

```powershell
# Script will validate automatically, or you can run:
./scripts/deploy-to-azure.ps1 -Action push-acr `
    -AcrName myacr `
    -SkipValidation
```

---

## 🚀 Quick Start

### Option 1: Fully Automated (All Steps)

```powershell
cd "C:\Users\herna\source\repos\DnsBlocker"

./scripts/deploy-to-azure.ps1 `
    -Action all `
    -AcrName myacr `
    -ResourceGroup dns-blocker-rg `
    -Location eastus `
    -ImageTag v1.0.0
```

**What happens:**
1. Validates prerequisites
2. Pushes image to ACR
3. Deploys to ACI for testing
4. Deploys Logic Apps scheduler
5. Prompts between steps

### Option 2: Individual Steps

```powershell
# Step 8 Only: Push to ACR
./scripts/deploy-to-azure.ps1 -Action push-acr -AcrName myacr

# Step 9 Only: Deploy to ACI
./scripts/deploy-to-azure.ps1 -Action deploy-aci `
    -AcrName myacr `
    -ResourceGroup dns-blocker-rg

# Step 10 Only: Deploy Scheduler
./scripts/deploy-to-azure.ps1 -Action deploy-scheduler `
    -AcrName myacr `
    -ResourceGroup dns-blocker-rg
```

---

## 📝 Detailed Steps

### STEP 8: Push Docker Image to ACR

```powershell
./scripts/deploy-to-azure.ps1 `
    -Action push-acr `
    -AcrName myacr `
    -ImageTag v1.0.0
```

**What it does:**
1. ✅ Verifies Docker image exists locally
2. ✅ Logs in to ACR
3. ✅ Tags image: `myacr.azurecr.io/importer:v1.0.0`
4. ✅ Pushes to ACR
5. ✅ Verifies in ACR

**Output:**
```
✓ Docker image found: importer:latest
✓ Logged in to ACR
✓ Image tagged
✓ Image pushed to ACR: myacr.azurecr.io/importer:v1.0.0
✓ Image verified in ACR
  Available tags: v1.0.0, latest
```

**Verify manually:**
```powershell
# List repositories
az acr repository list --name myacr

# List tags
az acr repository show-tags --name myacr --repository importer

# Show image details
az acr repository show --name myacr --repository importer
```

---

### STEP 9: Deploy and Test in ACI

```powershell
./scripts/deploy-to-azure.ps1 `
    -Action deploy-aci `
    -AcrName myacr `
    -ResourceGroup dns-blocker-rg `
    -Location eastus
```

**What it does:**
1. ✅ Creates/verifies resource group
2. ✅ Retrieves ACR credentials
3. ✅ Prompts for Azure Storage Connection String
4. ✅ Deploys container to ACI
5. ✅ Streams logs in real-time
6. ✅ Offers cleanup

**Output:**
```
Container deployed: importer-run-20250214-120000

═════════════════════════════════════════════════════════
[Phase 2] Starting parallel flush with 50 concurrent tasks
...
[Phase 2] ✓ Completed | Processed 52500 batches | Throughput: 18200 ops/s
✓ Import Pipeline completed successfully
═════════════════════════════════════════════════════════

Container status: Succeeded
```

**Container Configuration:**
```
Resource Group: dns-blocker-rg
Name: importer-run-20250214-120000
Image: myacr.azurecr.io/importer:v1.0.0
CPU: 1 core
Memory: 1 GB
Restart Policy: Never
Status: Succeeded (after import completes)
```

**Cleanup:**
```powershell
# Script offers automatic cleanup, or manual:
az container delete \
    --resource-group dns-blocker-rg \
    --name importer-run-20250214-120000 \
    --yes
```

**Verify:**
```powershell
# Check container status
az container show \
    --resource-group dns-blocker-rg \
    --name importer-run-20250214-120000 \
    --query "instanceView.state"

# Check logs
az container logs \
    --resource-group dns-blocker-rg \
    --name importer-run-20250214-120000 \
    --follow
```

---

### STEP 10: Deploy Weekly Scheduler (Logic Apps)

```powershell
./scripts/deploy-to-azure.ps1 `
    -Action deploy-scheduler `
    -AcrName myacr `
    -ResourceGroup dns-blocker-rg `
    -Location eastus
```

**What it does:**
1. ✅ Validates Bicep template
2. ✅ Compiles Bicep to ARM
3. ✅ Deploys Logic Apps workflow
4. ✅ Configures schedule: Weekly, Sunday 00:00 UTC

**Bicep Template Structure:**

```bicep
# main.bicep contains:
├─ HTTP Connection (for ARM API calls)
├─ Logic Apps Workflow with:
│  ├─ Trigger: Recurrence (Weekly, Sunday 00:00 UTC)
│  ├─ Action 1: Delete old container (cleanup)
│  ├─ Action 2: Wait 5 seconds
│  └─ Action 3: Create new container instance
└─ Outputs: LogicApp name, container details
```

**Output:**
```
✓ Bicep template validated
✓ Bicep template compiled to ARM
✓ Scheduler deployed successfully

Scheduler deployed: importer-scheduler
Logic Apps is configured to run weekly on Sunday at 00:00 UTC
```

**How it works:**
```
Every Sunday at 00:00 UTC:
  1. Delete old container instance (cleanup)
  2. Wait 5 seconds
  3. Create new container instance
     └─ Runs import (Hagezi + Tranco)
     └─ Process completes in ~15 minutes
     └─ Container automatically stops (RestartPolicy: Never)
```

**Verify in Portal:**
```
Azure Portal
└─ Logic Apps
   └─ importer-scheduler
      ├─ Runs (weekly execution history)
      ├─ Settings (schedule)
      └─ Designer (workflow)
```

---

## 📊 Complete Workflow

```
┌─────────────────────────┐
│   Docker Image Ready    │
│  (importer:latest)      │
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│   STEP 8: Push to ACR   │
│  ./deploy-to-azure.ps1  │
│   -Action push-acr      │
└────────────┬────────────┘
             │
      ✓ Image in Azure
             │
             ▼
┌─────────────────────────┐
│  STEP 9: Test in ACI    │
│  ./deploy-to-azure.ps1  │
│   -Action deploy-aci    │
└────────────┬────────────┘
             │
   ✓ Import runs successfully
   ✓ 5M domains imported
             │
             ▼
┌─────────────────────────┐
│ STEP 10: Deploy Schedule│
│  ./deploy-to-azure.ps1  │
│ -Action deploy-scheduler│
└────────────┬────────────┘
             │
✓ Every Sunday 00:00 UTC:
  └─ Import runs automatically
  └─ Container stops after
  └─ Cost: ~R$ 0.75/month
```

---

## 🔧 Advanced Usage

### Custom Parameters

```powershell
./scripts/deploy-to-azure.ps1 `
    -Action all `
    -AcrName myacr `
    -ResourceGroup my-custom-rg `
    -Location westus2 `
    -ImageTag v2.0.0 `
    -Environment Staging `
    -ContainerName custom-importer-run `
    -SkipValidation
```

### Using Configuration File

```powershell
# Create config.json
@{
    AcrName = "myacr"
    ResourceGroup = "dns-blocker-rg"
    Location = "eastus"
    ImageTag = "v1.0.0"
    Environment = "Production"
} | ConvertTo-Json | Out-File config.json

# Use config
$config = Get-Content config.json | ConvertFrom-Json
./scripts/deploy-to-azure.ps1 `
    -Action all `
    -AcrName $config.AcrName `
    -ResourceGroup $config.ResourceGroup `
    -Location $config.Location `
    -ImageTag $config.ImageTag
```

### Scheduled Deployment

```powershell
# Run deployment every morning
$trigger = New-JobTrigger -At "06:00" -Daily
Register-ScheduledJob `
    -Name "Deploy-Importer" `
    -Trigger $trigger `
    -ScriptBlock {
        & "C:\Users\herna\source\repos\DnsBlocker\scripts\deploy-to-azure.ps1" `
            -Action all -AcrName myacr -ResourceGroup dns-blocker-rg
    }
```

---

## 🐛 Troubleshooting

### Error: "Azure CLI not found"

```powershell
# Install Azure CLI
choco install azure-cli

# Or download from:
# https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-windows
```

### Error: "Not logged in to Azure"

```powershell
az login
az account set --subscription "Subscription Name"
```

### Error: "ACR login failed"

```powershell
# Verify ACR exists
az acr list --query "[].name"

# Check credentials
az acr credential show --name myacr

# Re-login
az acr login --name myacr
```

### Error: "Image not found in Docker"

```powershell
# Run build first
./scripts/build-importer.bat build

# Or verify image exists
docker images | Select-String importer
```

### Error: "Bicep template validation failed"

```powershell
# Check Bicep syntax
bicep build ./infra/main.bicep --outfile ./infra/main.json

# View detailed error
bicep build ./infra/main.bicep --outfile ./infra/main.json 2>&1
```

### Error: "Insufficient permissions"

```powershell
# Ensure correct subscription
az account show

# May need Contributor role on subscription
az role assignment list --include-inherited `
    --query "[?principalName=='your@email.com']" -o table
```

### High Memory Usage During Deploy

```powershell
# Increase ACI memory
./scripts/deploy-to-azure.ps1 `
    -Action deploy-aci `
    ... params ...

# Edit in script, change:
# $containerMemory = 2  # Instead of 1
```

---

## 📊 Cost Estimation

### Step 8: Push to ACR
```
Bandwidth: ~250 MB = ~$0.01
Registry Storage: 250 MB/month = ~$0.05
───────────────────────────────
Subtotal: ~$0.06/month
```

### Step 9: Test in ACI
```
One-time test:
CPU: 1 core × 15 min = $0.0000015 × 900 = $0.00135
Memory: 1 GB × 15 min = $0.0000036 × 900 = $0.00324
───────────────────────────────
Subtotal: ~$0.005 per test
```

### Step 10: Weekly Scheduler
```
Per week (1 run × 15 min):
CPU: 1 core × 15 min = $0.0000015 × 900 = $0.00135
Memory: 1 GB × 15 min = $0.0000036 × 900 = $0.00324
Logic Apps: Included in free tier

Per month (4 runs):
Compute: $0.00459 × 4 = $0.01836

Per year:
Compute: $0.01836 × 12 = ~$0.22

───────────────────────────────
TOTAL YEARLY: ~$2.69
TOTAL MONTHLY: ~$0.22
```

**Total Monthly Cost:**
```
ACR: $0.06
ACI: $0.22
Logic Apps: FREE
Storage: Existing
───────────────────
TOTAL: ~$0.28/month

vs BEFORE: R$ 250/month
SAVINGS: 99.9% ↓
```

---

## ✅ Deployment Checklist

- [ ] Prerequisites installed (docker, az, bicep)
- [ ] Logged in to Azure (`az login`)
- [ ] Docker image built (`docker images | grep importer`)
- [ ] ACR created (`az acr list`)
- [ ] Resource group exists
- [ ] Step 8: Image pushed to ACR
  - [ ] Verify: `az acr repository list --name myacr`
- [ ] Step 9: Container deployed to ACI
  - [ ] Verify logs: `az container logs --resource-group ... --name ...`
  - [ ] Status: Succeeded
- [ ] Step 10: Logic Apps scheduler deployed
  - [ ] Verify: Azure Portal → Logic Apps → importer-scheduler
  - [ ] Schedule: Sunday 00:00 UTC
- [ ] Test run completed successfully
- [ ] Monitor: Check execution history weekly

---

## 🎯 Next Steps

### After Deployment

1. **Monitor Executions**
   ```powershell
   # Check recent runs
   az logicapp trigger-history show `
       --name importer-scheduler `
       --resource-group dns-blocker-rg `
       --trigger-name Recurrence
   ```

2. **View Container Logs**
   ```powershell
   # Get latest container
   az container list --resource-group dns-blocker-rg `
       --query "sort_by([],&properties.createTime)[-1].name" -o tsv

   # View logs
   az container logs --resource-group dns-blocker-rg `
       --name <container-name>
   ```

3. **Verify Data Import**
   ```powershell
   # Query Table Storage
   # Check BlockedDomains, HageziGambling tables
   ```

4. **Setup Alerts** (Optional)
   ```powershell
   # Create alert for failed Logic App runs
   az monitor metrics alert create `
       --name "Importer-Failed" `
       --resource-group dns-blocker-rg `
       --resource-type "Microsoft.Logic/workflows" `
       --resource importer-scheduler
   ```

---

## 📞 Support

**Common Issues:**
- Build failed? Check: `docs/LOCAL_BUILD_GUIDE.md`
- ACR issues? Check: `docs/DOCKER_BUILD_GUIDE.md`
- Azure login? Run: `az login`

**Documentation:**
- `DEPLOYMENT_READY.md` - Overall guide
- `LOCAL_BUILD_GUIDE.md` - Build troubleshooting
- `DOCKER_BUILD_GUIDE.md` - Docker & ACI details

---

## 🎉 Success Indicators

```
✅ Image in ACR
✅ Container ran in ACI
✅ Import completed (logs show success)
✅ Logic Apps created
✅ Schedule configured (Sunday 00:00 UTC)
✅ Tables updated with domains

🚀 FULLY AUTOMATED DEPLOYMENT COMPLETE!
```

---

**Last Updated:** 2025-02-14
**Status:** Production Ready
**Cost:** ~$0.28/month
**Frequency:** Weekly (Sunday 00:00 UTC)

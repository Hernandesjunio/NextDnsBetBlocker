# Infrastructure as Code - NextDnsBetBlocker Deployment

> Bicep templates for Azure infrastructure deployment

## 📁 Files

- `main.bicep` - Main template with Logic Apps scheduler
- `parameters.example.json` - Example parameters
- `main.json` - Compiled ARM template (generated)

## 🚀 Quick Deploy

```powershell
# Via PowerShell script (recommended)
cd ../scripts
./deploy-to-azure.ps1 -Action deploy-scheduler `
    -AcrName myacr `
    -ResourceGroup dns-blocker-rg `
    -Location eastus

# Or manual via CLI
az deployment group create `
    --resource-group dns-blocker-rg `
    --template-file main.bicep `
    --parameters `
        acrName=myacr `
        imageTag=v1.0.0 `
        resourceGroupName=dns-blocker-rg `
        location=eastus
```

## 📋 Template Structure

```bicep
┌─ Parameters (inputs)
│  ├─ location
│  ├─ acrName
│  ├─ imageTag
│  └─ resourceGroupName
│
├─ Variables (computed values)
│
├─ Resources
│  ├─ HTTP Connection
│  └─ Logic Apps Workflow
│     ├─ Trigger: Recurrence (Weekly)
│     └─ Actions:
│        ├─ Delete old container
│        ├─ Wait 5 seconds
│        └─ Create new container
│
└─ Outputs (results)
```

## 🔧 Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `location` | string | resourceGroup().location | Azure region |
| `acrName` | string | - | ACR name (required) |
| `imageTag` | string | latest | Docker image tag |
| `resourceGroupName` | string | resourceGroup().name | Resource group |
| `logicAppName` | string | importer-scheduler | Logic Apps name |

## 📖 What Gets Deployed

### HTTP Connection
- Used by Logic Apps to call Azure APIs
- Enables arm API calls for container lifecycle management

### Logic Apps Workflow
- **Trigger**: Recurrence (Weekly, Sunday 00:00 UTC)
- **Action 1**: Delete old container (cleanup)
- **Action 2**: Wait 5 seconds
- **Action 3**: Create new container instance
  - Uses image from ACR
  - 1 CPU, 1 GB memory
  - Restart policy: Never
  - Environment: Production

## 🔐 Security Considerations

### Managed Identity
```bicep
// Logic Apps uses System-Assigned Managed Identity
// Credentials are retrieved from ACR via
listCredentials(acr.id, acr.apiVersion)
```

### Connection Strings
```bicep
// Storage connection string is injected as:
// - Secure environment variable
// - Passed at runtime
// - NOT hardcoded in template
```

### Role Assignment (Manual)
You may need to assign roles:
```powershell
# Get Logic Apps principal ID
$principalId = az logicapp show `
    --resource-group dns-blocker-rg `
    --name importer-scheduler `
    --query identity.principalId -o tsv

# Assign Contributor on resource group
az role assignment create `
    --assignee $principalId `
    --role Contributor `
    --resource-group dns-blocker-rg
```

## 🧪 Testing

### Validate Template
```powershell
# Validate Bicep
bicep build main.bicep --outfile main.json

# Validate ARM
az deployment group validate `
    --resource-group dns-blocker-rg `
    --template-file main.json
```

### Test Deploy
```powershell
# Dry run (what-if)
az deployment group what-if `
    --resource-group dns-blocker-rg `
    --template-file main.bicep `
    --parameters `
        acrName=myacr `
        imageTag=v1.0.0
```

## 📊 Bicep vs ARM

| Aspect | Bicep | ARM |
|--------|-------|-----|
| Syntax | DSL (like Terraform) | JSON |
| Readability | High | Low |
| Lines of Code | ~100 | ~300+ |
| Learning Curve | Moderate | Steep |
| Compilation | Compiled to ARM | Native |
| Validation | Same as ARM | Native |

## 🚀 Deployment Scenarios

### Scenario 1: First Time Setup
```powershell
./deploy-to-azure.ps1 `
    -Action all `
    -AcrName myacr `
    -ResourceGroup dns-blocker-rg
```

### Scenario 2: Update Image Tag
```powershell
# Push new image
docker tag importer:latest myacr.azurecr.io/importer:v2.0.0
docker push myacr.azurecr.io/importer:v2.0.0

# Re-deploy scheduler with new tag
./deploy-to-azure.ps1 `
    -Action deploy-scheduler `
    -AcrName myacr `
    -ImageTag v2.0.0
```

### Scenario 3: Change Schedule
Edit `main.bicep`:
```bicep
// Change from weekly Sunday to daily
scheduleFrequency: 'Day'
scheduleInterval: 1
// And update trigger schedule
```

### Scenario 4: Increase Container Resources
Edit `main.bicep`:
```bicep
var containerCpu = 2      // Increase from 1
var containerMemory = 2   // Increase from 1
```

## 📈 Cost Optimization

### Current Configuration
```bicep
containerCpu: 1
containerMemory: 1
```

**Cost per run:** ~$0.005 (15 minutes, 1 CPU, 1 GB)
**Cost per month:** ~$0.22 (4 runs)

### High Performance (if needed)
```bicep
containerCpu: 4
containerMemory: 4
```

**Cost per run:** ~$0.08 (15 minutes, 4 CPU, 4 GB)
**Cost per month:** ~$3.20 (4 runs)

## 🔄 Updates & Maintenance

### Update Schedule
```powershell
# Rerun deployment with new tag
./deploy-to-azure.ps1 `
    -Action deploy-scheduler `
    -ImageTag v2.0.0
```

### Rollback
```powershell
# Go back to previous version
./deploy-to-azure.ps1 `
    -Action deploy-scheduler `
    -ImageTag v1.0.0
```

### Delete Everything
```powershell
# Option 1: Delete Logic Apps only
az logicapp delete `
    --resource-group dns-blocker-rg `
    --name importer-scheduler

# Option 2: Delete entire resource group
az group delete --name dns-blocker-rg --yes
```

## 📚 References

- [Bicep Documentation](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [Logic Apps Templates](https://learn.microsoft.com/en-us/azure/logic-apps/quickstart-create-deploy-azure-resource-manager-template)
- [ACI Containers](https://learn.microsoft.com/en-us/azure/container-instances/)
- [Azure CLI Reference](https://learn.microsoft.com/en-us/cli/azure/)

---

**Version:** 1.0.0
**Last Updated:** 2025-02-14
**Status:** Production Ready

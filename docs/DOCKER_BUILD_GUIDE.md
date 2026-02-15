# NextDnsBetBlocker Importer - Docker Build Guide

> On-demand importer for DNS blocklists (Tranco, HaGeZi) that runs via Azure Container Instances (ACI) weekly.

## Overview

```
Console App → Docker Image → ACR → ACI (Scheduled)
  ↓              ↓              ↓         ↓
 .NET 10    Dockerfile     Registry    Weekly
```

## Prerequisites

1. **Docker** - [Install Docker Desktop](https://www.docker.com/products/docker-desktop)
2. **Azure CLI** - [Install Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
3. **Azure Container Registry (ACR)** - Already created in Azure

## Quick Start

### Build Locally

```bash
# Linux/macOS
./scripts/build-importer.sh build

# Windows
scripts\build-importer.bat build
```

### Test Locally

```bash
# Linux/macOS
./scripts/build-importer.sh test

# Windows
scripts\build-importer.bat test
```

### Push to ACR

```bash
# Linux/macOS
./scripts/build-importer.sh push myacr.azurecr.io v1.0.0

# Windows
scripts\build-importer.bat push myacr.azurecr.io v1.0.0
```

### Build and Push (All in One)

```bash
# Linux/macOS
./scripts/build-importer.sh all myacr.azurecr.io v1.0.0

# Windows
scripts\build-importer.bat all myacr.azurecr.io v1.0.0
```

## Step-by-Step Workflow

### 1️⃣ Build Docker Image

```bash
docker build -f src/NextDnsBetBlocker.Worker.Importer/Dockerfile \
  -t importer:latest \
  -t myacr.azurecr.io/importer:v1.0.0 \
  .
```

**What it does:**
- Multi-stage build (SDK for compile, Runtime for execute)
- Restore dependencies
- Compile .NET 10 application
- Creates minimal runtime image (~200MB)

### 2️⃣ Test Locally

```bash
docker run --rm importer:latest
```

**Expected output:**
```
═══════════════════════════════════════
   NextDnsBetBlocker Import Worker
   Running in ACI (Azure Container)
═══════════════════════════════════════
┌─────────────────────────────────────┐
│ Importing Hagezi                    │
└─────────────────────────────────────┘
✓ Hagezi import completed | Inserted: ... | Time: ...
...
✓ Import Pipeline completed successfully
```

### 3️⃣ Login to Azure

```bash
# Login to Azure
az login

# Set subscription (if needed)
az account set --subscription "Your Subscription Name"
```

### 4️⃣ Login to ACR

```bash
az acr login --name myacr
```

### 5️⃣ Push to ACR

```bash
docker push myacr.azurecr.io/importer:v1.0.0
docker push myacr.azurecr.io/importer:latest
```

## Dockerfile Explained

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
  - Copy .csproj files
  - dotnet restore
  - dotnet publish -c Release

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0
  - Copy published files
  - Set ENTRYPOINT
  - Result: ~200MB image
```

**Benefits:**
- ✅ Small image size (runtime only)
- ✅ No SDK bloat in production
- ✅ Fast execution
- ✅ Secure (no source code)

## Configuration

### Environment Variables

```bash
# Development (local)
ASPNETCORE_ENVIRONMENT=Development

# Production (ACI)
ASPNETCORE_ENVIRONMENT=Production
```

### App Configuration

Edit `appsettings.json`:

```json
{
  "ListImport": {
    "TrancoList": { ... },
    "Hagezi": { ... }
  },
  "ParallelImport": {
    "MaxDegreeOfParallelism": 50
  }
}
```

## Deployment to ACI

### Option 1: Azure Portal (Manual)

```
Azure Portal → Container Instances → Create
  ├─ Image source: Azure Container Registry
  ├─ Registry: myacr
  ├─ Image: importer
  ├─ Tag: v1.0.0
  ├─ CPU: 1 core
  ├─ Memory: 1 GB
  ├─ Restart policy: Never
  └─ Environment variables
```

### Option 2: Azure CLI (Recommended)

```bash
az container create \
  --resource-group mygroup \
  --name importer-run-1 \
  --image myacr.azurecr.io/importer:v1.0.0 \
  --registry-login-server myacr.azurecr.io \
  --registry-username <username> \
  --registry-password <password> \
  --cpu 1 \
  --memory 1 \
  --restart-policy Never \
  --environment-variables \
    ASPNETCORE_ENVIRONMENT=Production
```

### Option 3: Azure Logic Apps (Scheduled)

```
Logic Apps → Create
  ├─ Trigger: Recurrence (Weekly, Sundays 00:00)
  ├─ Action: Create or update container instance
  └─ Image: myacr.azurecr.io/importer:latest
```

## Monitoring

### Check Container Logs

```bash
# View logs
az container logs \
  --resource-group mygroup \
  --name importer-run-1

# Follow logs
az container logs \
  --resource-group mygroup \
  --name importer-run-1 \
  --follow
```

### Container Status

```bash
# Check status
az container show \
  --resource-group mygroup \
  --name importer-run-1 \
  --query "{state:instanceView.state}"

# Output: Success | Failed | Running
```

## Troubleshooting

### Issue: Build fails with "dotnet: not found"

**Solution:** Ensure Docker is running
```bash
docker --version  # Should show version
```

### Issue: Push fails with "unauthorized"

**Solution:** Login to ACR
```bash
az acr login --name myacr
```

### Issue: Container exits immediately

**Check logs:**
```bash
az container logs --resource-group mygroup --name importer-run-1
```

**Common causes:**
- Missing `appsettings.json`
- Invalid Azure connection string
- Network connectivity issues

### Issue: High memory usage

**Increase container memory:**
```bash
az container create ... --memory 2  # 2 GB
```

## Cost Estimation

```
ACI Cost:
├─ CPU: $0.0000015 per second → $0.39/month
├─ Memory: $0.0000036 per GB-second → $0.93/month (1GB)
├─ Storage: $0.05/month
└─ Total: ~$1.20/month for weekly 15-min runs
```

## Security

### Best Practices

1. **Never commit secrets**
   ```
   ❌ appsettings.json (if contains secrets)
   ✅ Environment variables (set in ACI)
   ```

2. **Use Managed Identity** (recommended)
   ```
   ACR → Managed Identity → No username/password needed
   ```

3. **Network isolation** (optional)
   ```
   ACR → Virtual Network → Private endpoint
   ```

## Next Steps

1. ✅ Build image locally
2. ✅ Test locally
3. ✅ Push to ACR
4. ✅ Deploy to ACI (manual test)
5. ✅ Create scheduler (Logic Apps)
6. ✅ Monitor and optimize

## References

- [Azure Container Instances Docs](https://learn.microsoft.com/en-us/azure/container-instances/)
- [Docker Multi-stage Builds](https://docs.docker.com/build/building/multi-stage/)
- [.NET in Docker](https://learn.microsoft.com/en-us/dotnet/core/docker/introduction)
- [Azure Container Registry](https://learn.microsoft.com/en-us/azure/container-registry/)

---

**Last Updated:** 2025-02-14
**Version:** 1.0.0
**Status:** Production Ready

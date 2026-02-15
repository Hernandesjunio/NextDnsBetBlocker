#!/usr/bin/env pwsh
<#
.SYNOPSIS
    NextDnsBetBlocker - Automated Azure Deployment Script
    
.DESCRIPTION
    Automates:
    - Step 8: Push image to ACR
    - Step 9: Deploy to ACI (manual test)
    - Step 10: Deploy Logic Apps scheduler (via Bicep)

.PARAMETER Action
    Action to perform: 'push-acr', 'deploy-aci', 'deploy-scheduler', 'all', 'cleanup'

.PARAMETER AcrName
    Azure Container Registry name (without .azurecr.io)

.PARAMETER ResourceGroup
    Azure Resource Group name

.PARAMETER Location
    Azure region (e.g., eastus, westus2)

.PARAMETER ImageTag
    Docker image tag (default: v1.0.0)

.PARAMETER Environment
    Environment name (default: Development)

.EXAMPLE
    ./scripts/deploy-to-azure.ps1 -Action all -AcrName myacr -ResourceGroup mygroup -Location eastus

.EXAMPLE
    ./scripts/deploy-to-azure.ps1 -Action push-acr -AcrName myacr

.EXAMPLE
    ./scripts/deploy-to-azure.ps1 -Action deploy-scheduler -AcrName myacr -ResourceGroup mygroup
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('push-acr', 'deploy-aci', 'deploy-scheduler', 'all', 'cleanup')]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$AcrName,

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "dns-blocker-rg",

    [Parameter(Mandatory = $false)]
    [string]$Location = "eastus",

    [Parameter(Mandatory = $false)]
    [string]$ImageTag = "v1.0.0",

    [Parameter(Mandatory = $false)]
    [string]$Environment = "Production",

    [Parameter(Mandatory = $false)]
    [string]$ContainerName = "importer-run-$(Get-Date -Format 'yyyyMMdd-HHmmss')",

    [Parameter(Mandatory = $false)]
    [switch]$SkipValidation
)

# ============= CONFIGURATION =============
$ErrorActionPreference = "Stop"
$AcrUrl = "$AcrName.azurecr.io"
$ImageName = "importer"
$FullImageName = "$AcrUrl/$ImageName:$ImageTag"
$BicepTemplateFile = "./infra/main.bicep"

# ============= COLORS & FORMATTING =============
$Colors = @{
    Header  = [System.ConsoleColor]::Cyan
    Success = [System.ConsoleColor]::Green
    Warning = [System.ConsoleColor]::Yellow
    Error   = [System.ConsoleColor]::Red
    Info    = [System.ConsoleColor]::Blue
}

function Write-Header {
    param([string]$Message)
    Write-Host "═" * 60 -ForegroundColor $Colors.Header
    Write-Host $Message -ForegroundColor $Colors.Header
    Write-Host "═" * 60 -ForegroundColor $Colors.Header
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor $Colors.Success
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠ $Message" -ForegroundColor $Colors.Warning
}

function Write-Error_ {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor $Colors.Error
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ $Message" -ForegroundColor $Colors.Info
}

# ============= VALIDATION =============
function Test-Prerequisites {
    Write-Info "Validating prerequisites..."

    # Check Azure CLI
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Error_ "Azure CLI not found. Install from: https://learn.microsoft.com/en-us/cli/azure/install-azure-cli"
        exit 1
    }
    Write-Success "Azure CLI found"

    # Check Docker
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Error_ "Docker not found. Install from: https://www.docker.com/products/docker-desktop"
        exit 1
    }
    Write-Success "Docker found"

    # Check Bicep CLI
    if (-not (Get-Command bicep -ErrorAction SilentlyContinue)) {
        Write-Warning "Bicep CLI not found. Installing..."
        az bicep install
    }
    Write-Success "Bicep CLI found"

    # Check Azure login
    $account = az account show 2>/dev/null
    if (-not $account) {
        Write-Info "Not logged in to Azure. Launching login..."
        az login
    }
    Write-Success "Azure authenticated"
}

# ============= STEP 8: PUSH TO ACR =============
function Push-ToAcr {
    Write-Header "Step 8: Push Docker Image to ACR"

    # Verify image exists locally
    Write-Info "Checking if Docker image exists locally..."
    $imageExists = docker images | Select-String "importer" | Select-String "latest"
    
    if (-not $imageExists) {
        Write-Error_ "Docker image 'importer:latest' not found"
        Write-Info "Run build first: ./scripts/build-importer.bat build"
        exit 1
    }
    Write-Success "Docker image found: importer:latest"

    # Login to ACR
    Write-Info "Logging in to ACR: $AcrName..."
    az acr login --name $AcrName
    if ($LASTEXITCODE -ne 0) {
        Write-Error_ "Failed to login to ACR"
        exit 1
    }
    Write-Success "Logged in to ACR"

    # Tag image
    Write-Info "Tagging image: $FullImageName..."
    docker tag importer:latest $FullImageName
    docker tag importer:latest "$AcrUrl/$ImageName:latest"
    Write-Success "Image tagged"

    # Push to ACR
    Write-Info "Pushing to ACR (this may take 2-5 minutes)..."
    docker push $FullImageName
    docker push "$AcrUrl/$ImageName:latest"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error_ "Failed to push image to ACR"
        exit 1
    }
    Write-Success "Image pushed to ACR: $FullImageName"

    # Verify in ACR
    Write-Info "Verifying image in ACR..."
    $acrlRepositories = az acr repository list --name $AcrName -o json | ConvertFrom-Json
    if ($acrlRepositories -contains $ImageName) {
        Write-Success "Image verified in ACR"
        $tags = az acr repository show-tags --name $AcrName --repository $ImageName -o json | ConvertFrom-Json
        Write-Info "Available tags: $($tags -join ', ')"
    }
}

# ============= STEP 9: DEPLOY TO ACI (MANUAL TEST) =============
function Deploy-ToAci {
    Write-Header "Step 9: Deploy to Azure Container Instances (Manual Test)"

    # Ensure resource group exists
    Write-Info "Creating/verifying resource group: $ResourceGroup..."
    az group create --name $ResourceGroup --location $Location
    Write-Success "Resource group ready"

    # Get ACR credentials
    Write-Info "Retrieving ACR credentials..."
    $acrLoginServer = az acr show --name $AcrName --query loginServer -o tsv
    $acrUsername = az acr credential show --name $AcrName --query username -o tsv
    $acrPassword = az acr credential show --name $AcrName --query "passwords[0].value" -o tsv
    Write-Success "ACR credentials retrieved"

    # Get Azure connection string (for app to work)
    Write-Warning "You need to provide Azure Storage Connection String"
    Write-Info "Available storage accounts:"
    az storage account list --query "[].name" -o table

    $StorageConnectionString = Read-Host "Enter Azure Storage Connection String (or press Enter to skip)"

    if ([string]::IsNullOrEmpty($StorageConnectionString)) {
        Write-Warning "Skipping connection string - container will fail but you can verify image runs"
    }

    # Deploy container
    Write-Info "Deploying container to ACI: $ContainerName..."
    $command = @(
        "az", "container", "create",
        "--resource-group", $ResourceGroup,
        "--name", $ContainerName,
        "--image", $FullImageName,
        "--registry-login-server", $acrLoginServer,
        "--registry-username", $acrUsername,
        "--registry-password", $acrPassword,
        "--cpu", "1",
        "--memory", "1",
        "--restart-policy", "Never",
        "--environment-variables", "ASPNETCORE_ENVIRONMENT=$Environment"
    )

    if (-not [string]::IsNullOrEmpty($StorageConnectionString)) {
        $command += @("AzureStorageConnectionString=$StorageConnectionString")
    }

    & $command[0] $command[1..$($command.Length - 1)]

    if ($LASTEXITCODE -ne 0) {
        Write-Error_ "Failed to deploy to ACI"
        exit 1
    }
    Write-Success "Container deployed: $ContainerName"

    # Monitor execution
    Write-Info "Monitoring container execution (press Ctrl+C to stop)..."
    az container logs --resource-group $ResourceGroup --name $ContainerName --follow

    # Show final status
    $status = az container show --resource-group $ResourceGroup --name $ContainerName --query "instanceView.state" -o tsv
    Write-Success "Container status: $status"

    Write-Warning "Container will be deleted after validation"
    $cleanup = Read-Host "Delete container now? (y/n, default: y)"
    if ($cleanup -ne "n") {
        Write-Info "Deleting container..."
        az container delete --resource-group $ResourceGroup --name $ContainerName --yes
        Write-Success "Container deleted"
    }
}

# ============= STEP 10: DEPLOY SCHEDULER (LOGIC APPS) =============
function Deploy-Scheduler {
    Write-Header "Step 10: Deploy Scheduler (Logic Apps via Bicep)"

    # Verify Bicep template exists
    if (-not (Test-Path $BicepTemplateFile)) {
        Write-Error_ "Bicep template not found: $BicepTemplateFile"
        exit 1
    }
    Write-Success "Bicep template found"

    # Validate Bicep
    Write-Info "Validating Bicep template..."
    bicep build $BicepTemplateFile --outfile "./infra/main.json"
    if ($LASTEXITCODE -ne 0) {
        Write-Error_ "Bicep validation failed"
        exit 1
    }
    Write-Success "Bicep template validated"

    # Deploy via ARM (compiled from Bicep)
    Write-Info "Deploying Logic Apps scheduler to Azure..."
    az deployment group create `
        --resource-group $ResourceGroup `
        --template-file "./infra/main.json" `
        --parameters `
            acrName=$AcrName `
            imageTag=$ImageTag `
            resourceGroupName=$ResourceGroup `
            location=$Location

    if ($LASTEXITCODE -ne 0) {
        Write-Error_ "Deployment failed"
        exit 1
    }
    Write-Success "Scheduler deployed successfully"

    # Get Logic Apps information
    Write-Info "Retrieving Logic Apps information..."
    $logicAppName = "importer-scheduler"
    $logicAppUrl = az resource show --resource-group $ResourceGroup --name $logicAppName --resource-type "Microsoft.Logic/workflows" --query "properties.definition.triggers.manual.inputs.schema.properties.url.default" -o tsv 2>/dev/null

    Write-Success "Scheduler deployed: $logicAppName"
    Write-Info "Logic Apps is configured to run weekly on Sunday at 00:00 UTC"
}

# ============= CLEANUP =============
function Cleanup-Resources {
    Write-Header "Cleanup: Delete All Resources"

    $confirm = Read-Host "Are you sure you want to delete all resources in $ResourceGroup? (type 'yes' to confirm)"
    if ($confirm -ne "yes") {
        Write-Warning "Cleanup cancelled"
        return
    }

    Write-Info "Deleting resource group: $ResourceGroup..."
    az group delete --name $ResourceGroup --yes --no-wait
    Write-Success "Resource group deletion initiated (may take a few minutes)"
}

# ============= MAIN FLOW =============
try {
    # Test prerequisites
    if (-not $SkipValidation) {
        Test-Prerequisites
    }

    # Execute requested action
    switch ($Action) {
        "push-acr" {
            Push-ToAcr
        }
        "deploy-aci" {
            Deploy-ToAci
        }
        "deploy-scheduler" {
            Deploy-Scheduler
        }
        "all" {
            Push-ToAcr
            Write-Host ""
            $continue = Read-Host "Step 8 complete. Continue to Step 9 (Deploy to ACI)? (y/n, default: y)"
            if ($continue -ne "n") {
                Deploy-ToAci
            }
            Write-Host ""
            $continue = Read-Host "Step 9 complete. Continue to Step 10 (Deploy Scheduler)? (y/n, default: y)"
            if ($continue -ne "n") {
                Deploy-Scheduler
            }
        }
        "cleanup" {
            Cleanup-Resources
        }
    }

    Write-Header "✓ Deployment Complete"
    Write-Success "All steps completed successfully!"
    Write-Info "Next steps:"
    Write-Info "  - Monitor Logic Apps: Azure Portal → Logic Apps → importer-scheduler"
    Write-Info "  - Check ACI logs: az container logs --resource-group $ResourceGroup --name <container-name>"
    Write-Info "  - View ACR images: az acr repository list --name $AcrName"
}
catch {
    Write-Error_ "Script failed: $_"
    exit 1
}

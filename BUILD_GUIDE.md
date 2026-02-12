# Build & Compilation Guide

## Prerequisites

- **.NET 10 SDK** - Download from https://dotnet.microsoft.com/download/dotnet/10.0
- **Git** (optional, for version control)
- **Docker** (optional, for containerized deployment)

Verify installation:
```bash
dotnet --version
```

## Building the Solution

### Option 1: Build All Projects (Recommended)

```bash
# Restore dependencies
dotnet restore NextDnsBetBlocker.sln

# Build solution
dotnet build NextDnsBetBlocker.sln -c Release

# Build with verbose output (if issues)
dotnet build NextDnsBetBlocker.sln -c Release -v detailed
```

### Option 2: Build Individual Projects

```bash
# Build Core library
dotnet build src/NextDnsBetBlocker.Core/NextDnsBetBlocker.Core.csproj -c Release

# Build Worker
dotnet build src/NextDnsBetBlocker.Worker/NextDnsBetBlocker.Worker.csproj -c Release

# Build Function App
dotnet build src/NextDnsBetBlocker.FunctionApp/NextDnsBetBlocker.FunctionApp.csproj -c Release
```

### Option 3: Quick Local Build (Development)

```bash
# Build in Debug mode (faster, not optimized)
dotnet build NextDnsBetBlocker.sln
```

## Project Dependencies

### NextDnsBetBlocker.Core (Shared Library)
```
├── Azure.Data.Tables 12.8.0
├── Azure.Storage.Blobs 12.19.0
├── Microsoft.Extensions.DependencyInjection.Abstractions 8.0.0
├── Microsoft.Extensions.Http 8.0.0
└── Microsoft.Extensions.Logging.Abstractions 8.0.0
```

### NextDnsBetBlocker.Worker (Console Application)
```
├── (includes all Core dependencies)
├── Microsoft.Extensions.Hosting 8.0.0
└── Microsoft.Extensions.Logging.Console 8.0.0
```

### NextDnsBetBlocker.FunctionApp (Azure Functions)
```
├── (includes all Core dependencies)
├── Microsoft.Azure.Functions.Extensions 1.1.0
├── Microsoft.Azure.WebJobs.Extensions.Storage 5.1.3
└── Microsoft.NET.Sdk.Functions 4.4.0
```

## Publishing/Releasing

### Publish for Local Deployment

```bash
# Publish Worker for standalone use
dotnet publish src/NextDnsBetBlocker.Worker \
  -c Release \
  -o ./publish/worker \
  --self-contained false

# Run published application
./publish/worker/NextDnsBetBlocker.Worker
```

### Publish for Azure Functions

```bash
# Publish Function App
dotnet publish src/NextDnsBetBlocker.FunctionApp \
  -c Release \
  -o ./publish/functions \
  --self-contained false

# Deploy to Azure Functions
func azure functionapp publish MyFunctionApp --build remote
```

### Self-Contained Deployments

```bash
# Windows deployment
dotnet publish src/NextDnsBetBlocker.Worker \
  -c Release \
  -o ./publish/worker-windows \
  -r win-x64 \
  --self-contained true

# Linux deployment
dotnet publish src/NextDnsBetBlocker.Worker \
  -c Release \
  -o ./publish/worker-linux \
  -r linux-x64 \
  --self-contained true

# macOS deployment
dotnet publish src/NextDnsBetBlocker.Worker \
  -c Release \
  -o ./publish/worker-macos \
  -r osx-x64 \
  --self-contained true
```

## Docker Build

### Build Docker Image for Worker

```bash
# Build image
docker build -f src/NextDnsBetBlocker.Worker/Dockerfile \
  -t nextdns-bet-blocker:latest \
  .

# Build with specific tag
docker build -f src/NextDnsBetBlocker.Worker/Dockerfile \
  -t nextdns-bet-blocker:1.0.0 \
  .

# Build with docker-compose
docker-compose build --no-cache

# Build with progress output
docker build -f src/NextDnsBetBlocker.Worker/Dockerfile \
  -t nextdns-bet-blocker:latest \
  --progress=plain \
  .
```

### Run Docker Image

```bash
# Run locally
docker run --rm \
  -e NextDnsApiKey="your_key" \
  -e NextDnsProfileId="your_profile" \
  -v $(pwd)/data:/app/data \
  nextdns-bet-blocker:latest

# Run with docker-compose
docker-compose up --build

# Run in background
docker-compose up -d --build

# Run with custom environment
docker run --rm \
  -e NextDnsApiKey="your_key" \
  -e NextDnsProfileId="your_profile" \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -v $(pwd)/data:/app/data \
  nextdns-bet-blocker:latest
```

## Testing & Validation

### Validate Solution Structure

```bash
# Check project references
dotnet list reference

# Check package versions
dotnet list package

# Check for deprecated packages
dotnet list package --outdated
```

### Build Validation

```bash
# Check for build warnings
dotnet build NextDnsBetBlocker.sln /p:TreatWarningsAsErrors=true

# Check code analysis
dotnet build NextDnsBetBlocker.sln /p:EnforceCodeStyleInBuild=true

# Full diagnostic build
dotnet build NextDnsBetBlocker.sln -v diagnostic
```

### Runtime Validation

```bash
# Run Worker (local test)
cd src/NextDnsBetBlocker.Worker
dotnet run --configuration Release

# With custom environment
ASPNETCORE_ENVIRONMENT=Development dotnet run

# Check logs for errors
dotnet run 2>&1 | tee build.log
```

## Build Troubleshooting

### Common Issues

**Issue: "Project file not found"**
```bash
# Verify solution exists
test -f NextDnsBetBlocker.sln && echo "Found" || echo "Not found"

# List project files
find . -name "*.csproj"
```

**Issue: "Package restore failed"**
```bash
# Clear NuGet cache
dotnet nuget locals all --clear

# Restore again
dotnet restore NextDnsBetBlocker.sln

# Restore with diagnostics
dotnet restore --verbosity diagnostic
```

**Issue: "Build failed with errors"**
```bash
# Get detailed error output
dotnet build NextDnsBetBlocker.sln -v detailed

# Check .NET version compatibility
dotnet --version  # Should be 10.x

# Verify all projects build
dotnet build src/NextDnsBetBlocker.Core -c Release
dotnet build src/NextDnsBetBlocker.Worker -c Release
dotnet build src/NextDnsBetBlocker.FunctionApp -c Release
```

**Issue: "Docker build fails"**
```bash
# Build with verbose output
docker build --progress=plain -t test .

# Check Dockerfile syntax
docker build --dry-run .

# View Dockerfile content
cat src/NextDnsBetBlocker.Worker/Dockerfile
```

## Build Output

### Release Build Artifacts

```
publish/
├── worker/
│   ├── NextDnsBetBlocker.Worker.dll
│   ├── NextDnsBetBlocker.Core.dll
│   ├── appsettings.json
│   └── (other dependencies)
│
└── functions/
    ├── ProcessLogsFunction.dll
    ├── UpdateHageziFunction.dll
    ├── host.json
    └── (other dependencies)
```

### Build Configuration

**Debug** (default, slower execution):
```bash
dotnet build NextDnsBetBlocker.sln -c Debug
```

**Release** (optimized, faster execution):
```bash
dotnet build NextDnsBetBlocker.sln -c Release
```

**Recommended for production**: Always use Release configuration.

## Deployment Package Creation

### Create Deployment Package

```bash
# Create zip for Windows deployment
dotnet publish src/NextDnsBetBlocker.Worker \
  -c Release \
  -o ./publish/worker

zip -r nextdns-worker-windows.zip ./publish/worker

# Create tar for Linux deployment
dotnet publish src/NextDnsBetBlocker.Worker \
  -c Release \
  -o ./publish/worker \
  -r linux-x64

tar -czf nextdns-worker-linux.tar.gz ./publish/worker

# Create Docker image
docker build -f src/NextDnsBetBlocker.Worker/Dockerfile \
  -t nextdns-bet-blocker:1.0.0 \
  .

docker save nextdns-bet-blocker:1.0.0 | gzip > nextdns-docker.tar.gz
```

## CI/CD Integration

### GitHub Actions Example

Create `.github/workflows/build.yml`:

```yaml
name: Build

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      
      - run: dotnet restore
      - run: dotnet build -c Release
      - run: dotnet test
      - run: dotnet publish -c Release -o ./publish
```

### Azure Pipelines Example

Create `azure-pipelines.yml`:

```yaml
trigger:
  - main

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'
  dotnetVersion: '10.0.x'

steps:
  - task: UseDotNet@2
    inputs:
      version: $(dotnetVersion)
  
  - task: DotNetCoreCLI@2
    inputs:
      command: 'restore'
      projects: '**/*.csproj'
  
  - task: DotNetCoreCLI@2
    inputs:
      command: 'build'
      arguments: '-c $(buildConfiguration)'
  
  - task: DotNetCoreCLI@2
    inputs:
      command: 'publish'
      publishWebProjects: false
      arguments: '-c $(buildConfiguration) -o $(Build.ArtifactStagingDirectory)'
```

## Performance Optimization

### Build Optimization

```bash
# Parallel build (faster on multi-core systems)
dotnet build --parallel NextDnsBetBlocker.sln

# Incremental build
dotnet build NextDnsBetBlocker.sln

# Clean before build
dotnet clean NextDnsBetBlocker.sln && dotnet build -c Release

# Skip unused projects
dotnet build src/NextDnsBetBlocker.Core -c Release
dotnet build src/NextDnsBetBlocker.Worker -c Release
```

### Docker Build Optimization

```bash
# Use multi-stage build (already in Dockerfile)
# Reduces final image size

# Build with cache
docker build -f src/NextDnsBetBlocker.Worker/Dockerfile \
  -t nextdns-bet-blocker:latest \
  --cache-from nextdns-bet-blocker:latest \
  .
```

## Maintenance & Updates

### Update NuGet Packages

```bash
# Check for updates
dotnet list package --outdated

# Update specific package
dotnet add src/NextDnsBetBlocker.Core/NextDnsBetBlocker.Core.csproj \
  package Azure.Data.Tables --version 12.9.0

# Update all packages
dotnet list package --outdated | grep -oP '^\s+\K\S+' | \
  xargs -I {} dotnet add package {}
```

### Update .NET Runtime

```bash
# Check current version
dotnet --version

# Install new .NET version
# Visit: https://dotnet.microsoft.com/download/dotnet

# Verify installation
dotnet --list-sdks
dotnet --list-runtimes
```

## Summary

**Quick Build**:
```bash
dotnet build NextDnsBetBlocker.sln -c Release
```

**Quick Run**:
```bash
./setup.sh && docker-compose up --build
```

**Quick Publish**:
```bash
dotnet publish src/NextDnsBetBlocker.Worker -c Release -o ./publish
```

All build commands are straightforward with standard .NET CLI.

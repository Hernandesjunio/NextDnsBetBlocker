# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Set environment variables to disable NuGet fallback and cache issues
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    NUGET_XMLDOC_MODE=skip \
    MSBUILDENSURESTDOUTFORTASKLOGGERS=1 \
    DOTNET_NOLOGO=true

# Copy project files only
COPY ["src/NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj", "NextDnsBetBlocker.Worker.Importer/"]
COPY ["src/NextDnsBetBlocker.Core/NextDnsBetBlocker.Core.csproj", "NextDnsBetBlocker.Core/"]

# Clean build artifacts that may contain cached fallback folder references
RUN rm -rf NextDnsBetBlocker.Worker.Importer/obj NextDnsBetBlocker.Worker.Importer/bin \
    && rm -rf NextDnsBetBlocker.Core/obj NextDnsBetBlocker.Core/bin

# Restore dependencies with settings to ignore fallback folder issues
RUN dotnet restore "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" \
    --disable-build-servers \
    /p:RestoreIgnoreFailed=true \
    /p:TreatWarningsAsErrors=false

# Copy source code
COPY ["src/NextDnsBetBlocker.Worker.Importer/", "NextDnsBetBlocker.Worker.Importer/"]
COPY ["src/NextDnsBetBlocker.Core/", "NextDnsBetBlocker.Core/"]

# Build and publish
RUN dotnet publish "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --self-contained false \
    /p:PublishTrimmed=false \
    /p:PublishReadyToRun=false \
    /p:DebugType=none \
    /p:DebugSymbols=false \
    /p:TreatWarningsAsErrors=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0

WORKDIR /app

# Copy published app from builder
COPY --from=builder /app/publish .

# Copy appsettings
COPY ["src/NextDnsBetBlocker.Worker.Importer/appsettings.json", "./"]

# Run the application
ENTRYPOINT ["dotnet", "NextDnsBetBlocker.Worker.Importer.dll"]

# Labels for documentation
LABEL maintainer="NextDnsBetBlocker"
LABEL description="On-demand importer for DNS blocklists. Runs via ACI weekly."
LABEL version="1.0.0"COPY ["src/NextDnsBetBlocker.Worker.Importer/appsettings.json", "./"]

# Run the application
ENTRYPOINT ["dotnet", "NextDnsBetBlocker.Worker.Importer.dll"]

# Labels for documentation
LABEL maintainer="NextDnsBetBlocker"
LABEL description="On-demand importer for DNS blocklists. Runs via ACI weekly."
LABEL version="1.0.0"

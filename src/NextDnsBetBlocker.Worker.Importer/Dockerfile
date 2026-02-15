# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy project files only
COPY ["src/NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj", "NextDnsBetBlocker.Worker.Importer/"]
COPY ["src/NextDnsBetBlocker.Core/NextDnsBetBlocker.Core.csproj", "NextDnsBetBlocker.Core/"]

# Restore dependencies with environment settings
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

RUN dotnet restore "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" \
    --disable-build-servers

# Copy source code
COPY ["src/NextDnsBetBlocker.Worker.Importer/", "NextDnsBetBlocker.Worker.Importer/"]
COPY ["src/NextDnsBetBlocker.Core/", "NextDnsBetBlocker.Core/"]

# Build and publish
RUN dotnet publish "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --disable-build-servers

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
LABEL version="1.0.0"

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution and project files
COPY ["src/NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj", "NextDnsBetBlocker.Worker.Importer/"]
COPY ["src/NextDnsBetBlocker.Core/NextDnsBetBlocker.Core.csproj", "NextDnsBetBlocker.Core/"]

# Restore dependencies
RUN dotnet restore "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj"

# Copy source code
COPY ["src/NextDnsBetBlocker.Worker.Importer/", "NextDnsBetBlocker.Worker.Importer/"]
COPY ["src/NextDnsBetBlocker.Core/", "NextDnsBetBlocker.Core/"]

# Build and publish
RUN dotnet publish "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0

WORKDIR /app

# Copy published app from builder
COPY --from=builder /app/publish .

# Create config directory
RUN mkdir -p /app/config

# Copy appsettings (will be overridden at runtime via volume or env)
COPY ["src/NextDnsBetBlocker.Worker.Importer/appsettings.json", "./"]

# Run the application
ENTRYPOINT ["dotnet", "NextDnsBetBlocker.Worker.Importer.dll"]

# Labels for documentation
LABEL maintainer="NextDnsBetBlocker"
LABEL description="On-demand importer for DNS blocklists. Runs via ACI weekly."
LABEL version="1.0.0"

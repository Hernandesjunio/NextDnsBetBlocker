# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution files
COPY ["Directory.Build.props", "."]
COPY ["./.editorconfig", "."]

# Copy project files
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

# Copy appsettings
COPY ["src/NextDnsBetBlocker.Worker.Importer/appsettings.json", "./"]

# Health check (optional)
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=1 \
  CMD dotnet --version || exit 1

# Run the application
ENTRYPOINT ["dotnet", "NextDnsBetBlocker.Worker.Importer.dll"]

# Labels for documentation
LABEL maintainer="NextDnsBetBlocker"
LABEL description="On-demand importer for DNS blocklists. Runs via ACI weekly."
LABEL version="1.0.0"

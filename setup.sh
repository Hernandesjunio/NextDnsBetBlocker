#!/bin/bash

# NextDNS Bet Blocker - Setup Script
# This script helps with initial configuration

set -e

echo "=== NextDNS Bet Blocker Setup ==="
echo

# Check if .env file exists
if [ ! -f .env ]; then
    echo "Creating .env from .env.example..."
    cp .env.example .env
    echo "✓ .env created"
    echo
fi

# Prompt for NextDNS credentials
echo "=== NextDNS Configuration ==="
echo "Get your API Key and Profile ID from: https://nextdns.io/settings/api"
echo

read -p "Enter your NextDNS API Key: " API_KEY
read -p "Enter your NextDNS Profile ID: " PROFILE_ID

# Update .env file
sed -i "s/your_nextdns_api_key_here/$API_KEY/" .env
sed -i "s/your_nextdns_profile_id_here/$PROFILE_ID/" .env

echo "✓ NextDNS credentials saved"
echo

# Check Docker
echo "=== Environment Check ==="
if ! command -v docker &> /dev/null; then
    echo "✗ Docker not found. Please install Docker Desktop."
    exit 1
fi
echo "✓ Docker installed"

if ! command -v docker-compose &> /dev/null; then
    echo "✗ Docker Compose not found. Please install Docker Compose."
    exit 1
fi
echo "✓ Docker Compose installed"

# Check .NET
if ! command -v dotnet &> /dev/null; then
    echo "✗ .NET SDK not found. Please install .NET 10 SDK."
    exit 1
fi
DOTNET_VERSION=$(dotnet --version)
echo "✓ .NET SDK installed: $DOTNET_VERSION"

echo

# Create directories
mkdir -p data
echo "✓ Created data directory"

# Initialize allowlist if not exists
if [ ! -f src/NextDnsBetBlocker.Worker/allowlist.txt ]; then
    echo "# NextDNS Bet Blocker - Local Allowlist" > src/NextDnsBetBlocker.Worker/allowlist.txt
    echo "# Add one domain per line. Lines starting with # are comments." >> src/NextDnsBetBlocker.Worker/allowlist.txt
    echo "✓ Created allowlist.txt"
fi

echo

echo "=== Setup Complete! ==="
echo
echo "Next steps:"
echo "1. Review and update .env file if needed"
echo "2. Add domains to src/NextDnsBetBlocker.Worker/allowlist.txt (optional)"
echo "3. Run: docker-compose up --build"
echo
echo "The worker will start processing logs every 30 minutes."
echo "View logs with: docker-compose logs -f worker"
echo

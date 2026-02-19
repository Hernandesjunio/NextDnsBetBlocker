#!/bin/bash

# Script para criar Service Principal automaticamente
# Usage: ./scripts/setup-service-principal.sh

set -e

echo "=================================================="
echo "NextDnsBetBlocker - Service Principal Setup"
echo "=================================================="
echo ""

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    echo "❌ Azure CLI not found. Please install it first."
    echo "   https://docs.microsoft.com/cli/azure/install-azure-cli"
    exit 1
fi

# Check if already logged in
if ! az account show &> /dev/null; then
    echo "🔐 Logging into Azure..."
    az login
fi

# Get subscription info
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
SUBSCRIPTION_NAME=$(az account show --query name -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)

echo "ℹ️  Using Subscription: $SUBSCRIPTION_NAME ($SUBSCRIPTION_ID)"
echo ""

# Create Service Principal
echo "🔄 Creating Service Principal..."
SP_NAME="NextDnsBetBlocker-GitHub-Deploy"

# Check if SP already exists
if az ad sp list --filter "displayname eq '$SP_NAME'" --query "[0].appId" -o tsv 2>/dev/null | grep -q .; then
    echo "⚠️  Service Principal '$SP_NAME' already exists."
    EXISTING_APP_ID=$(az ad sp list --filter "displayname eq '$SP_NAME'" --query "[0].appId" -o tsv)
    read -p "Do you want to create a new secret for existing SP? (y/n) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo "🔄 Creating new secret..."
        SECRET_JSON=$(az ad app credential reset --id $EXISTING_APP_ID --output json)
        APP_ID=$EXISTING_APP_ID
    else
        echo "Exiting..."
        exit 0
    fi
else
    echo "Creating new Service Principal..."
    SECRET_JSON=$(az ad sp create-for-rbac \
        --name "$SP_NAME" \
        --role "Contributor" \
        --scopes /subscriptions/$SUBSCRIPTION_ID \
        --output json)
    APP_ID=$(echo $SECRET_JSON | jq -r '.appId')
fi

echo "✅ Service Principal created successfully!"
echo ""

# Extract credentials
CLIENT_ID=$(echo $SECRET_JSON | jq -r '.appId')
CLIENT_SECRET=$(echo $SECRET_JSON | jq -r '.password')

# Display credentials for GitHub Secrets
echo "=================================================="
echo "📋 GitHub Secrets to Add:"
echo "=================================================="
echo ""
echo "1️⃣  AZURE_CREDENTIALS (JSON format):"
echo "   Copy and paste the following JSON as-is:"
echo ""
echo "{"
echo "  \"clientId\": \"$CLIENT_ID\","
echo "  \"clientSecret\": \"$CLIENT_SECRET\","
echo "  \"subscriptionId\": \"$SUBSCRIPTION_ID\","
echo "  \"tenantId\": \"$TENANT_ID\""
echo "}"
echo ""
echo "2️⃣  AZURE_SUBSCRIPTION_ID:"
echo "   $SUBSCRIPTION_ID"
echo ""
echo "3️⃣  AZURE_RESOURCE_GROUP:"
echo "   (Choose a name, e.g., dnsblocker-rg)"
echo ""
echo "4️⃣  AZURE_LOCATION:"
echo "   (Choose a location, e.g., eastus, westus2, northeurope)"
echo ""

# Instructions
echo "=================================================="
echo "📝 Next Steps:"
echo "=================================================="
echo ""
echo "1. Go to your GitHub repository"
echo "2. Settings > Secrets and variables > Actions"
echo "3. Add each secret above"
echo ""
echo "4. (Optional) Verify Service Principal:"
echo "   az ad sp show --id $APP_ID"
echo ""
echo "5. Run the 'Provision Azure Infrastructure' workflow"
echo "   GitHub > Actions > Provision Azure Infrastructure > Run workflow"
echo ""

# Offer to copy to clipboard (macOS/Linux)
if command -v pbcopy &> /dev/null; then
    read -p "Copy AZURE_CREDENTIALS JSON to clipboard? (y/n) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo "{\"clientId\": \"$CLIENT_ID\",\"clientSecret\": \"$CLIENT_SECRET\",\"subscriptionId\": \"$SUBSCRIPTION_ID\",\"tenantId\": \"$TENANT_ID\"}" | pbcopy
        echo "✅ Copied to clipboard!"
    fi
elif command -v xclip &> /dev/null; then
    read -p "Copy AZURE_CREDENTIALS JSON to clipboard? (y/n) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo "{\"clientId\": \"$CLIENT_ID\",\"clientSecret\": \"$CLIENT_SECRET\",\"subscriptionId\": \"$SUBSCRIPTION_ID\",\"tenantId\": \"$TENANT_ID\"}" | xclip -selection clipboard
        echo "✅ Copied to clipboard!"
    fi
fi

echo ""
echo "=================================================="
echo "✅ Setup completed!"
echo "=================================================="

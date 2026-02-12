#!/bin/bash

# Script to test NextDNS connectivity and configuration

set -e

# Load environment variables
if [ -f .env ]; then
    export $(cat .env | xargs)
fi

API_KEY="${NEXTDNS_API_KEY:-}"
PROFILE_ID="${NEXTDNS_PROFILE_ID:-}"

if [ -z "$API_KEY" ] || [ -z "$PROFILE_ID" ]; then
    echo "Error: NEXTDNS_API_KEY and NEXTDNS_PROFILE_ID must be set"
    echo "Please configure .env file"
    exit 1
fi

echo "=== Testing NextDNS Configuration ==="
echo

echo "API Key: ${API_KEY:0:8}...${API_KEY: -4}"
echo "Profile ID: $PROFILE_ID"
echo

# Test API connectivity
echo "Testing NextDNS API connectivity..."
RESPONSE=$(curl -s -X GET "https://api.nextdns.io/profiles/$PROFILE_ID/logs?limit=1" \
    -H "X-Api-Key: $API_KEY" \
    -w "\n%{http_code}")

HTTP_CODE=$(echo "$RESPONSE" | tail -n1)
BODY=$(echo "$RESPONSE" | head -n-1)

if [ "$HTTP_CODE" = "200" ]; then
    echo "✓ Successfully connected to NextDNS"
    echo "✓ API Key is valid"
    echo "✓ Profile ID is valid"
    echo
    echo "Sample log entry:"
    echo "$BODY" | head -50
else
    echo "✗ Failed to connect to NextDNS"
    echo "HTTP Status: $HTTP_CODE"
    echo "Response: $BODY"
    exit 1
fi

echo
echo "=== Configuration Test Passed ==="

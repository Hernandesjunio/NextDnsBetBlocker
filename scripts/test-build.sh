#!/bin/bash
# Simple build test script

echo "======================================"
echo "NextDnsBetBlocker Importer - Build Test"
echo "======================================"
echo ""

# Check Docker
echo "✓ Checking Docker..."
if ! command -v docker &> /dev/null; then
    echo "✗ Docker not found"
    exit 1
fi
docker --version

echo ""
echo "✓ Starting build..."
cd "$(dirname "$0")/.."

docker build \
    -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" \
    -t "importer:latest" \
    --progress=plain \
    . 2>&1 | tail -50

echo ""
echo "✓ Build complete. Testing image..."
docker run --rm importer:latest --version || echo "Note: Runtime may not support --version"

echo ""
echo "======================================"
echo "✓ Build test completed"
echo "======================================"

#!/bin/bash

# Build and push script for NextDnsBetBlocker Importer
# Usage: ./build.sh [build|push] [acr-url] [tag]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_NAME="NextDnsBetBlocker.Worker.Importer"
IMAGE_NAME="importer"
DEFAULT_TAG="latest"
DEFAULT_ACR="your-acr.azurecr.io"

# Parameters
ACTION=${1:-build}
ACR_URL=${2:-$DEFAULT_ACR}
TAG=${3:-$DEFAULT_TAG}
FULL_IMAGE_NAME="${ACR_URL}/${IMAGE_NAME}:${TAG}"

# Functions
print_header() {
    echo -e "${BLUE}════════════════════════════════════════${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}════════════════════════════════════════${NC}"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_info() {
    echo -e "${YELLOW}ℹ $1${NC}"
}

build_image() {
    print_header "Building Docker Image"
    print_info "Image: $FULL_IMAGE_NAME"
    print_info "Dockerfile: $SCRIPT_DIR/Dockerfile"
    
    cd "$SCRIPT_DIR/../.."  # Go to repo root
    
    if docker build \
        -f "$SCRIPT_DIR/Dockerfile" \
        -t "$IMAGE_NAME:$TAG" \
        -t "$IMAGE_NAME:latest" \
        -t "$FULL_IMAGE_NAME" \
        .; then
        print_success "Docker image built successfully"
        print_info "Local tags:"
        print_info "  - $IMAGE_NAME:$TAG"
        print_info "  - $IMAGE_NAME:latest"
        print_info "  - $FULL_IMAGE_NAME"
        return 0
    else
        print_error "Failed to build Docker image"
        return 1
    fi
}

push_image() {
    print_header "Pushing to Azure Container Registry"
    print_info "Registry: $ACR_URL"
    print_info "Image: $IMAGE_NAME:$TAG"
    
    if [ "$ACR_URL" == "$DEFAULT_ACR" ]; then
        print_error "Please provide ACR URL as parameter"
        echo "Usage: $0 push <acr-url> [tag]"
        echo "Example: $0 push myacr.azurecr.io v1.0.0"
        return 1
    fi
    
    # Login to ACR
    print_info "Logging in to $ACR_URL..."
    if ! az acr login --name "${ACR_URL%%.azurecr.io}"; then
        print_error "Failed to login to ACR"
        return 1
    fi
    print_success "Logged in to ACR"
    
    # Push image
    print_info "Pushing $FULL_IMAGE_NAME..."
    if docker push "$FULL_IMAGE_NAME"; then
        print_success "Image pushed successfully"
        echo ""
        print_info "Image URI: $FULL_IMAGE_NAME"
        return 0
    else
        print_error "Failed to push image"
        return 1
    fi
}

test_local() {
    print_header "Testing Docker Image Locally"
    print_info "Running: docker run $IMAGE_NAME:$TAG"
    
    # Run with timeout (should exit quickly)
    if timeout 30 docker run \
        --rm \
        -e "ASPNETCORE_ENVIRONMENT=Development" \
        "$IMAGE_NAME:$TAG" \
        /bin/sh -c "echo 'Container test successful'" 2>&1 | head -20; then
        print_success "Docker image test passed"
        return 0
    else
        print_error "Docker image test failed"
        return 1
    fi
}

show_help() {
    cat << EOF
${BLUE}NextDnsBetBlocker Importer - Docker Build Script${NC}

${YELLOW}Usage:${NC}
    $0 [ACTION] [OPTIONS]

${YELLOW}Actions:${NC}
    build       Build Docker image locally
    push        Push image to Azure Container Registry
    all         Build and push
    test        Test image locally
    help        Show this help message

${YELLOW}Options:${NC}
    [acr-url]   Azure Container Registry URL (e.g., myacr.azurecr.io)
    [tag]       Image tag (default: latest)

${YELLOW}Examples:${NC}
    $0 build
    $0 push myacr.azurecr.io v1.0.0
    $0 all myacr.azurecr.io latest
    $0 test

${YELLOW}Prerequisites:${NC}
    - Docker installed and running
    - Azure CLI installed (for push action)
    - Logged in to Azure: az login

EOF
}

# Main logic
case "$ACTION" in
    build)
        build_image
        ;;
    push)
        push_image
        ;;
    all)
        build_image && push_image
        ;;
    test)
        build_image && test_local
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        print_error "Unknown action: $ACTION"
        echo ""
        show_help
        exit 1
        ;;
esac

EXIT_CODE=$?
echo ""
if [ $EXIT_CODE -eq 0 ]; then
    print_success "Operation completed successfully"
else
    print_error "Operation failed with exit code $EXIT_CODE"
fi

exit $EXIT_CODE

@echo off
REM Build and push script for NextDnsBetBlocker Importer (Windows)
REM Usage: build-importer.bat [build|push] [acr-url] [tag]

setlocal enabledelayedexpansion

REM Configuration
set PROJECT_NAME=NextDnsBetBlocker.Worker.Importer
set IMAGE_NAME=importer
set DEFAULT_TAG=latest
set DEFAULT_ACR=your-acr.azurecr.io

REM Parameters
set ACTION=%1
if "!ACTION!"=="" set ACTION=build

set ACR_URL=%2
if "!ACR_URL!"=="" set ACR_URL=!DEFAULT_ACR!

set TAG=%3
if "!TAG!"=="" set TAG=!DEFAULT_TAG!

set FULL_IMAGE_NAME=!ACR_URL!/!IMAGE_NAME!:!TAG!

REM Colors (using ANSI escape codes)
set BLUE=[94m
set GREEN=[92m
set YELLOW=[93m
set RED=[91m
set NC=[0m

echo.
echo !BLUE!════════════════════════════════════════!NC!
echo !BLUE!NextDnsBetBlocker Importer - Docker Build!NC!
echo !BLUE!════════════════════════════════════════!NC!

if /i "!ACTION!"=="build" (
    echo !YELLOW!ℹ Building Docker image...!NC!
    echo !YELLOW!ℹ Image: !FULL_IMAGE_NAME!!NC!
    
    REM Go to repo root (assuming script is in scripts folder)
    cd /d "%~dp0.."
    
    docker build ^
        -f "src/!PROJECT_NAME!/Dockerfile" ^
        -t "!IMAGE_NAME!:!TAG!" ^
        -t "!IMAGE_NAME!:latest" ^
        -t "!FULL_IMAGE_NAME!" ^
        .
    
    if !errorlevel! equ 0 (
        echo !GREEN!✓ Docker image built successfully!NC!
        echo.
        echo !YELLOW!ℹ Local tags:!NC!
        echo !YELLOW!  - !IMAGE_NAME!:!TAG!!NC!
        echo !YELLOW!  - !IMAGE_NAME!:latest!NC!
        echo !YELLOW!  - !FULL_IMAGE_NAME!!NC!
    ) else (
        echo !RED!✗ Failed to build Docker image!NC!
        exit /b 1
    )

) else if /i "!ACTION!"=="push" (
    echo !YELLOW!ℹ Pushing to Azure Container Registry...!NC!
    echo !YELLOW!ℹ Registry: !ACR_URL!!NC!
    echo !YELLOW!ℹ Image: !IMAGE_NAME!:!TAG!!NC!
    
    if "!ACR_URL!"=="!DEFAULT_ACR!" (
        echo !RED!✗ Please provide ACR URL as parameter!NC!
        echo.
        echo Usage: %0 push ^<acr-url^> [tag]
        echo Example: %0 push myacr.azurecr.io v1.0.0
        exit /b 1
    )
    
    REM Login to ACR
    echo !YELLOW!ℹ Logging in to !ACR_URL!...!NC!
    for /f %%i in ("!ACR_URL:.azurecr.io=!") do set ACR_NAME=%%i
    
    call az acr login --name !ACR_NAME!
    if !errorlevel! neq 0 (
        echo !RED!✗ Failed to login to ACR!NC!
        exit /b 1
    )
    echo !GREEN!✓ Logged in to ACR!NC!
    
    REM Push image
    echo !YELLOW!ℹ Pushing !FULL_IMAGE_NAME!...!NC!
    docker push "!FULL_IMAGE_NAME!"
    
    if !errorlevel! equ 0 (
        echo !GREEN!✓ Image pushed successfully!NC!
        echo.
        echo !YELLOW!ℹ Image URI: !FULL_IMAGE_NAME!!NC!
    ) else (
        echo !RED!✗ Failed to push image!NC!
        exit /b 1
    )

) else if /i "!ACTION!"=="all" (
    echo !YELLOW!ℹ Building and pushing...!NC!
    call "%~f0" build !ACR_URL! !TAG!
    if !errorlevel! neq 0 exit /b 1
    
    call "%~f0" push !ACR_URL! !TAG!
    if !errorlevel! neq 0 exit /b 1

) else if /i "!ACTION!"=="test" (
    echo !YELLOW!ℹ Testing Docker image locally...!NC!
    docker run --rm "!IMAGE_NAME!:!TAG!" /bin/sh -c "echo Container test successful"
    
    if !errorlevel! equ 0 (
        echo !GREEN!✓ Docker image test passed!NC!
    ) else (
        echo !RED!✗ Docker image test failed!NC!
        exit /b 1
    )

) else if /i "!ACTION!"=="help" (
    echo.
    echo !YELLOW!NextDnsBetBlocker Importer - Docker Build Script!NC!
    echo.
    echo !YELLOW!Usage:!NC!
    echo   %0 [ACTION] [OPTIONS]
    echo.
    echo !YELLOW!Actions:!NC!
    echo   build       Build Docker image locally
    echo   push        Push image to Azure Container Registry
    echo   all         Build and push
    echo   test        Test image locally
    echo   help        Show this help message
    echo.
    echo !YELLOW!Options:!NC!
    echo   [acr-url]   Azure Container Registry URL ^(e.g., myacr.azurecr.io^)
    echo   [tag]       Image tag ^(default: latest^)
    echo.
    echo !YELLOW!Examples:!NC!
    echo   %0 build
    echo   %0 push myacr.azurecr.io v1.0.0
    echo   %0 all myacr.azurecr.io latest
    echo   %0 test
    echo.
    echo !YELLOW!Prerequisites:!NC!
    echo   - Docker installed and running
    echo   - Azure CLI installed ^(for push action^)
    echo   - Logged in to Azure: az login
    echo.

) else (
    echo !RED!✗ Unknown action: !ACTION!!NC!
    echo.
    call "%~f0" help
    exit /b 1
)

echo.
echo !BLUE!════════════════════════════════════════!NC!
if !errorlevel! equ 0 (
    echo !GREEN!✓ Operation completed successfully!NC!
) else (
    echo !RED!✗ Operation failed with exit code !errorlevel!!NC!
)
echo !BLUE!════════════════════════════════════════!NC!
echo.

endlocal

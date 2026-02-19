@echo off
REM Script para criar Service Principal no Windows
REM Usage: scripts\setup-service-principal.bat

echo ==================================================
echo NextDnsBetBlocker - Service Principal Setup
echo ==================================================
echo.

REM Check if Azure CLI is installed
az --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Error: Azure CLI not found
    echo Please install from: https://docs.microsoft.com/cli/azure/install-azure-cli
    pause
    exit /b 1
)

REM Login to Azure
echo Logging into Azure...
call az login

REM Get subscription info
for /f "delims=" %%i in ('az account show --query id -o tsv') do set SUBSCRIPTION_ID=%%i
for /f "delims=" %%i in ('az account show --query name -o tsv') do set SUBSCRIPTION_NAME=%%i
for /f "delims=" %%i in ('az account show --query tenantId -o tsv') do set TENANT_ID=%%i

echo.
echo Using Subscription: %SUBSCRIPTION_NAME% (%SUBSCRIPTION_ID%)
echo.

REM Create Service Principal
echo Creating Service Principal...
set SP_NAME=NextDnsBetBlocker-GitHub-Deploy

for /f "delims=" %%i in ('az ad sp create-for-rbac --name "%SP_NAME%" --role "Contributor" --scopes /subscriptions/%SUBSCRIPTION_ID% --query appId -o tsv 2^>nul') do set APP_ID=%%i

if "%APP_ID%"=="" (
    echo Service Principal may already exist or an error occurred
    pause
    exit /b 1
)

REM Get full credentials
for /f "delims=" %%i in ('az ad sp create-for-rbac --name "%SP_NAME%" --role "Contributor" --scopes /subscriptions/%SUBSCRIPTION_ID% --output json') do set JSON_OUTPUT=%%i

echo.
echo Service Principal created successfully!
echo.
echo ==================================================
echo GitHub Secrets to Add:
echo ==================================================
echo.
echo 1. AZURE_CREDENTIALS (JSON format):
echo %JSON_OUTPUT%
echo.
echo 2. AZURE_SUBSCRIPTION_ID:
echo %SUBSCRIPTION_ID%
echo.
echo 3. AZURE_RESOURCE_GROUP:
echo (Choose a name, e.g., dnsblocker-rg)
echo.
echo 4. AZURE_LOCATION:
echo (Choose a location, e.g., eastus, westus2, northeurope)
echo.
echo ==================================================
echo Next Steps:
echo ==================================================
echo.
echo 1. Copy the JSON above
echo 2. Go to GitHub repository Settings
echo 3. Secrets and variables ^> Actions
echo 4. Create new repository secret named "AZURE_CREDENTIALS"
echo 5. Paste the JSON value
echo.
pause

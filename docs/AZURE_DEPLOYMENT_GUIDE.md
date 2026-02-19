# Azure Deployment Guide - NextDnsBetBlocker FunctionApp

Complete documentation para configurar e fazer deploy da Azure Function App.

## 📋 Índice

1. [Pré-requisitos](#pré-requisitos)
2. [Setup Service Principal](#setup-service-principal)
3. [Configurar GitHub Secrets](#configurar-github-secrets)
4. [Provisionar Infraestrutura](#provisionar-infraestrutura)
5. [Fazer Deploy](#fazer-deploy)
6. [Configurar App Settings](#configurar-app-settings)
7. [Troubleshooting](#troubleshooting)

---

## 🔧 Pré-requisitos

- ✅ Conta Azure ativa com subscription
- ✅ Azure CLI instalado (`az --version`)
- ✅ Git instalado
- ✅ Permissões de Owner/Contributor na subscription

---

## 🔐 Setup Service Principal

### Opção 1: Usando Azure CLI (Recomendado)

```bash
# 1. Fazer login no Azure
az login

# 2. Definir subscription (se tiver múltiplas)
az account set --subscription "YOUR_SUBSCRIPTION_ID"

# 3. Criar Service Principal
az ad sp create-for-rbac \
  --name "NextDnsBetBlocker-GitHub-Deploy" \
  --role "Contributor" \
  --scopes /subscriptions/YOUR_SUBSCRIPTION_ID \
  --output json
```

**Output esperado:**
```json
{
  "appId": "00000000-0000-0000-0000-000000000000",
  "displayName": "NextDnsBetBlocker-GitHub-Deploy",
  "password": "YOUR_CLIENT_SECRET_HERE",
  "tenant": "00000000-0000-0000-0000-000000000000"
}
```

### Opção 2: Usando Azure Portal

1. Ir para **Azure Active Directory > App registrations > New registration**
2. Nome: `NextDnsBetBlocker-GitHub-Deploy`
3. Criar certificado/secret em **Certificates & secrets**
4. Atribuir role na **Subscription > IAM > Add role assignment**
   - Role: `Contributor`
   - Member: Seu app registration

---

## 🔑 Configurar GitHub Secrets

No seu repositório GitHub, adicione os seguintes secrets:

### Secrets Essenciais

1. **AZURE_CREDENTIALS** (do Service Principal)
   ```json
   {
     "clientId": "YOUR_APP_ID",
     "clientSecret": "YOUR_CLIENT_SECRET",
     "subscriptionId": "YOUR_SUBSCRIPTION_ID",
     "tenantId": "YOUR_TENANT_ID"
   }
   ```
   
   **Como adicionar:**
   - Settings > Secrets and variables > Actions > New repository secret
   - Nome: `AZURE_CREDENTIALS`
   - Valor: Cole o JSON acima (sem formatação)

2. **AZURE_SUBSCRIPTION_ID**
   ```
   YOUR_SUBSCRIPTION_ID
   ```

3. **AZURE_RESOURCE_GROUP**
   ```
   dnsblocker-rg
   ```

4. **AZURE_FUNCTION_APP_NAME**
   ```
   dnsblocker-fnapp
   ```

5. **AZURE_STORAGE_ACCOUNT_NAME**
   ```
   dnsblockersa
   ```

6. **AZURE_APP_SERVICE_PLAN_NAME**
   ```
   dnsblocker-plan
   ```

7. **AZURE_LOCATION**
   ```
   eastus
   ```

8. **AZURE_FUNCTION_APP_PUBLISH_PROFILE_STAGING**
   - Obtém em Azure Portal > Function App > Deployment slots > staging > Overview > "Get publish profile"
   - Abra o XML e copie todo conteúdo

---

## 🚀 Provisionar Infraestrutura

### Via GitHub Actions (Recomendado)

1. Ir para **Actions > Provision Azure Infrastructure**
2. Clicar em **Run workflow**
3. Selecionar environment: `prod` ou `staging`
4. Clicar em **Run workflow**

**O que será criado:**
- ✅ Resource Group
- ✅ Storage Account
- ✅ Function App (production)
- ✅ Deployment Slot (staging)
- ✅ App Service Plan (Consumption)
- ✅ Blob container: `function-locks`

### Via Azure CLI (Manual)

```bash
# Definir variáveis
RESOURCE_GROUP="dnsblocker-rg"
LOCATION="eastus"
FUNCTION_APP_NAME="dnsblocker-fnapp"
STORAGE_ACCOUNT="dnsblockersa"
APP_SERVICE_PLAN="dnsblocker-plan"

# Criar resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Deploy bicep template
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file .github/bicep/function-app.bicep \
  --parameters \
    functionAppName=$FUNCTION_APP_NAME \
    storageAccountName=$STORAGE_ACCOUNT \
    appServicePlanName=$APP_SERVICE_PLAN
```

---

## 📦 Fazer Deploy

### Trigger automático (Recomendado)

```bash
# 1. Commitar suas mudanças
git add .
git commit -m "Feature: xyz"
git push

# 2. Criar uma tag (dispara deploy automático)
git tag -a v1.0.0 -m "Release version 1.0.0"
git push origin v1.0.0
```

O workflow `deploy.yml` será executado automaticamente:
1. Build → 2. Deploy Staging → 3. Swap Production → 4. Create Release

### Manual via GitHub Actions

1. Ir para **Actions > Deploy Azure Function App**
2. Clicar em **Run workflow**
3. Inserir a tag para deploy (ex: `v1.0.0`)
4. Clicar em **Run workflow**

---

## ⚙️ Configurar App Settings

Após provisionar, configure as variáveis de ambiente da Function App:

### Via Azure Portal

1. Função App > Configuration > Application settings
2. Adicionar as seguintes settings:

```
NEXTDNS_API_KEY              = your-api-key
NEXTDNS_PROFILE_ID           = your-profile-id
NEXTDNS_BASE_URL             = https://api.nextdns.io
AZURE_STORAGE_CONNECTION_STR = DefaultEndpointsProtocol=https;...
HAGEZI_CACHE_INTERVAL_HOURS  = 24
RATE_LIMIT_PER_SECOND        = 5
```

### Via Azure CLI

```bash
az functionapp config appsettings set \
  --resource-group dnsblocker-rg \
  --name dnsblocker-fnapp \
  --settings \
    NEXTDNS_API_KEY="your-api-key" \
    NEXTDNS_PROFILE_ID="your-profile-id" \
    AZURE_STORAGE_CONNECTION_STR="your-connection-string"
```

### Via GitHub Actions (Future Enhancement)

Você pode adicionar um step nos workflows para configurar app settings automaticamente.

---

## 🔄 Fluxo de Deployment (Zero-Downtime)

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Push tag v1.0.0                                          │
└────────────────────┬────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. GitHub Actions: Build & Test                             │
│    - Checkout code                                          │
│    - Build .NET 10                                          │
│    - Publish artifact                                       │
└────────────────────┬────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. Deploy to Staging Slot                                   │
│    - Download artifact                                      │
│    - Deploy código ao slot "staging"                        │
│    - Function App roda em staging.dnsblocker-fnapp.azurewebsites.net
└────────────────────┬────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ 4. Manual Verification (Opcional)                           │
│    - Testar staging slot                                    │
│    - Validar Application Insights logs                      │
└────────────────────┬────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ 5. Blue-Green Swap                                          │
│    - Trocar produção ↔ staging                              │
│    - Produção recebe o código novo                          │
│    - ZERO DOWNTIME ✅                                       │
└────────────────────┬────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ 6. GitHub Release criado automaticamente                    │
│    - Tag: v1.0.0                                            │
│    - Release Notes gerado                                   │
└─────────────────────────────────────────────────────────────┘
```

---

## 🆘 Troubleshooting

### Erro: "Service Principal not found"

```bash
# Verificar se SP existe
az ad sp list --filter "displayname eq 'NextDnsBetBlocker-GitHub-Deploy'"

# Se não existe, criar novamente
az ad sp create-for-rbac --name "NextDnsBetBlocker-GitHub-Deploy"
```

### Erro: "Insufficient privileges"

Service Principal precisa de permissão `Contributor` na subscription:

```bash
# Adicionar role
az role assignment create \
  --assignee "YOUR_APP_ID" \
  --role "Contributor" \
  --scope /subscriptions/YOUR_SUBSCRIPTION_ID
```

### Erro: "Deployment slot swap failed"

```bash
# Verificar slot existe
az functionapp deployment slot list \
  --resource-group dnsblocker-rg \
  --name dnsblocker-fnapp

# Se não existe, criar
az functionapp deployment slot create \
  --resource-group dnsblocker-rg \
  --name dnsblocker-fnapp \
  --slot staging
```

### Erro: "Storage Account name already in use"

Names de Storage Account devem ser únicos globalmente no Azure:

```bash
# Usar timestamp para garantir unicidade
STORAGE_ACCOUNT="dnsblocker$(date +%s)"
```

### Função não inicia no Staging/Production

1. Verificar **Application Insights > Logs**
   ```kusto
   traces
   | where timestamp > ago(1h)
   | where severityLevel >= 1
   ```

2. Verificar **Configuration > Application settings**
   - Confirmar `NEXTDNS_API_KEY`, `NEXTDNS_PROFILE_ID`, etc.

3. Verificar **Monitor > Logs**
   - Procurar por erros de inicialização

---

## 📚 Próximos Passos

1. ✅ Configurar Service Principal
2. ✅ Adicionar GitHub Secrets
3. ✅ Executar `provision.yml` workflow
4. ✅ Configurar app settings no Azure
5. ✅ Criar e pushar tag para trigger deploy: `git tag -a v1.0.0 -m "Release 1.0.0" && git push origin v1.0.0`
6. ✅ Monitorar deployment via GitHub Actions
7. ✅ Verificar logs no Application Insights

---

## 📞 Suporte

Para dúvidas sobre:
- **Azure Functions**: https://docs.microsoft.com/azure/azure-functions/
- **GitHub Actions**: https://docs.github.com/actions
- **Bicep**: https://docs.microsoft.com/azure/azure-resource-manager/bicep/
- **Service Principal**: https://docs.microsoft.com/cli/azure/ad/sp

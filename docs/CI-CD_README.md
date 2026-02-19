# CI/CD Deployment Pipeline - NextDnsBetBlocker FunctionApp

## 📋 Overview

Este repositório contém uma esteira completa de **Infrastructure as Code (IaC)** e **CI/CD** para deployar a Azure Function App usando:

- **Bicep**: Templates de infraestrutura
- **GitHub Actions**: Workflows de provisioning e deployment
- **Azure Service Principal**: Autenticação segura
- **Deployment Slots**: Zero-downtime deployments (blue-green strategy)

---

## 🎯 Arquitetura

```
GitHub Repository
├── .github/
│   ├── workflows/
│   │   ├── provision.yml          ← Provisiona recursos (manual)
│   │   └── deploy.yml             ← Deploy código (tags/releases)
│   └── bicep/
│       └── function-app.bicep     ← IaC template
├── scripts/
│   ├── setup-service-principal.sh ← Setup script (Linux/Mac)
│   └── setup-service-principal.bat← Setup script (Windows)
└── docs/
    └── AZURE_DEPLOYMENT_GUIDE.md  ← Documentação completa
```

---

## 🚀 Quick Start

### 1️⃣ Setup Service Principal

**Linux/Mac:**
```bash
chmod +x scripts/setup-service-principal.sh
./scripts/setup-service-principal.sh
```

**Windows:**
```cmd
scripts\setup-service-principal.bat
```

### 2️⃣ Configure GitHub Secrets

Após executar o script, vá para:
- **Settings > Secrets and variables > Actions**
- Adicione os secrets:
  - `AZURE_CREDENTIALS` (JSON do Service Principal)
  - `AZURE_SUBSCRIPTION_ID`
  - `AZURE_RESOURCE_GROUP`
  - `AZURE_FUNCTION_APP_NAME`
  - `AZURE_STORAGE_ACCOUNT_NAME`
  - `AZURE_APP_SERVICE_PLAN_NAME`
  - `AZURE_LOCATION`

### 3️⃣ Provisionar Infraestrutura

- **GitHub Actions** > **Provision Azure Infrastructure**
- Clique em **Run workflow**
- Selecione `prod` ou `staging`

### 4️⃣ Deploy Código

```bash
# Criar tag (dispara deploy automático)
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0
```

O workflow fará:
1. ✅ Build .NET 10
2. ✅ Deploy para staging slot
3. ✅ Swap production (blue-green)
4. ✅ Criar release no GitHub

---

## 📚 Documentação

Para **setup detalhado**, consulte: [AZURE_DEPLOYMENT_GUIDE.md](docs/AZURE_DEPLOYMENT_GUIDE.md)

### Seções cobertas:
- ✅ Pré-requisitos
- ✅ Setup Service Principal (Azure CLI + Portal)
- ✅ GitHub Secrets configuration
- ✅ Provisioning via Bicep
- ✅ Deployment strategies
- ✅ App Settings management
- ✅ Troubleshooting

---

## 🔄 Fluxo de Deployment

```
1. git tag v1.0.0 && git push origin v1.0.0
                    ↓
2. GitHub Actions dispara automaticamente
                    ↓
3. Build .NET 10 + Publish
                    ↓
4. Deploy para Staging Slot
                    ↓
5. Blue-Green Swap (Production recebe novo código)
                    ↓
6. GitHub Release criada automaticamente
                    ↓
✅ Production rodando v1.0.0 (ZERO DOWNTIME)
```

---

## 🔐 Segurança

- ✅ Service Principal com permissão `Contributor`
- ✅ Secrets armazenados no GitHub Secrets (encrypted)
- ✅ HTTPS obrigatório na Function App
- ✅ TLS 1.2 mínimo no Storage Account
- ✅ System Managed Identity na Function App

---

## 📊 Workflows

### provision.yml
- **Trigger**: Manual (workflow_dispatch)
- **Ambiente**: prod ou staging
- **Ação**: Cria Function App, Storage, Deployment Slots
- **Tempo**: ~5-10 minutos

### deploy.yml
- **Trigger**: Automático em tags `v*` ou manual
- **Jobs**: Build → Deploy Staging → Swap Production
- **Cache**: NuGet dependencies
- **Tempo**: ~10-15 minutos

---

## 📝 Requisitos

- Azure CLI 2.40+
- .NET 10 SDK
- Git
- Conta GitHub
- Azure Subscription com permissões de Owner/Contributor

---

## 🛠️ Recursos Criados

### By Bicep Template:
- ✅ **Function App** (Consumption Plan)
- ✅ **Deployment Slot** (staging)
- ✅ **App Service Plan** (Dynamic/Y1)
- ✅ **Storage Account** (Standard_LRS)
- ✅ **Blob Container** (function-locks para distributed lock)

### By Workflows:
- ✅ **Build artifacts** (7 dias retenção)
- ✅ **GitHub Releases** (auto-criadas em tags)

---

## 🆘 Troubleshooting

**Erro: Service Principal not found**
```bash
az ad sp create-for-rbac --name "NextDnsBetBlocker-GitHub-Deploy"
```

**Erro: Deployment slot swap failed**
```bash
az functionapp deployment slot list \
  --resource-group YOUR_RG \
  --name YOUR_FUNCTION_APP
```

Para mais: Consulte [AZURE_DEPLOYMENT_GUIDE.md#troubleshooting](docs/AZURE_DEPLOYMENT_GUIDE.md#-troubleshooting)

---

## 📞 Suporte

- **Azure Functions Docs**: https://docs.microsoft.com/azure/azure-functions/
- **GitHub Actions Docs**: https://docs.github.com/actions
- **Bicep Docs**: https://learn.microsoft.com/azure/azure-resource-manager/bicep/

---

## 📜 Licença

Este projeto está sob licença MIT.

---

**Criado em**: 2024  
**Versão**: 1.0.0  
**Status**: ✅ Production Ready

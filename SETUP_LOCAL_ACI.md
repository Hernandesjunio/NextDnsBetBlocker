# NextDns Bet Blocker - Setup Local & Deploy ACI

## 🚀 Setup Local com Docker

### Pré-requisitos
- Docker instalado
- Azure Storage Connection String

### 1. Configurar variáveis de ambiente

```bash
# Copiar o arquivo de exemplo
cp .env.example .env

# Editar .env com suas credenciais
# Windows (PowerShell):
notepad .env

# Linux/Mac:
nano .env
```

Exemplo de `.AzureStorageConnectionString`:
```
ListImport__AzureStorageConnectionString=DefaultEndpointsProtocol=https;AccountName=seu_storage;AccountKey=sua_chave;EndpointSuffix=core.windows.net
```

### 2. Rodar o Importer localmente

**Opção A: Usando Docker Compose**
```bash
# Com o Azurite (Azure Storage emulado)
docker-compose --profile importer up --build

# Ou apenas rebuild
docker-compose --profile importer up --build --no-cache
```

**Opção B: Usando Docker CLI direto**
```bash
docker run --env-file .env \
  -e ASPNETCORE_ENVIRONMENT=Development \
  nextdns-importer:latest
```

### 3. Rodar o Worker localmente

```bash
# Inicia Azurite + Worker
docker-compose up

# Parar
docker-compose down
```

---

## 🔐 Deploy no ACI (Azure Container Instances)

### Pré-requisitos
- GitHub Secrets configuradas:
  - `AZURE_CREDENTIALS` - Service Principal para autenticar no Azure
  - `AZURE_STORAGE_CONNECTION_STRING` - Connection string do Storage Account
  - `AZURE_RESOURCE_GROUP` - Nome do resource group
  - `AZURE_REGISTRY_LOGIN_SERVER` - URL do Azure Container Registry
  - `AZURE_REGISTRY_USERNAME` - Username do ACR
  - `AZURE_REGISTRY_PASSWORD` - Password do ACR

### 1. Setup das Secrets no GitHub

1. Vá para: **Settings → Secrets and variables → Actions → New repository secret**

2. Crie as seguintes secrets:

#### AZURE_CREDENTIALS
```bash
az ad sp create-for-rbac --name "nextdns-importer-deploy" --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/{resource-group}
```

Copie a saída JSON como valor da secret.

#### AZURE_STORAGE_CONNECTION_STRING
```
DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net
```

#### Demais Secrets
- `AZURE_RESOURCE_GROUP`: Nome do seu resource group
- `AZURE_REGISTRY_LOGIN_SERVER`: Ex: `myregistry.azurecr.io`
- `AZURE_REGISTRY_USERNAME`: Username do ACR
- `AZURE_REGISTRY_PASSWORD`: Password do ACR

### 2. Disparar o Workflow

1. Vá para: **Actions → Deploy Importer to ACI**
2. Clique em **Run workflow**
3. Escolha o ambiente (staging/production)
4. Clique em **Run workflow**

O workflow irá:
- ✅ Build da imagem Docker
- ✅ Injetar `ListImport__AzureStorageConnectionString` como env var
- ✅ Deploy no ACI
- ✅ Executar uma vez (`restart-policy Never`)

### 3. Monitorar o Deploy

```bash
# Listar containers
az container list --resource-group seu-resource-group

# Ver logs
az container logs --resource-group seu-resource-group --name nextdns-importer

# Ver status
az container show --resource-group seu-resource-group --name nextdns-importer
```

---

## 📝 Estrutura de Arquivos

```
.
├── .env                              # ❌ NÃO COMMITAR (local apenas)
├── .env.example                      # ✅ Template para .env
├── .gitignore                        # Já inclui .env
├── docker-compose.yml                # Para dev local
├── .github/workflows/
│   └── deploy-importer-aci.yml      # Workflow do GitHub Actions
├── src/
│   ├── NextDnsBetBlocker.Worker/
│   ├── NextDnsBetBlocker.Worker.Importer/
│   │   ├── Dockerfile
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── NextDnsBetBlocker.Core/
└── README.md
```

---

## 🔍 Troubleshooting

### Docker run local falha com "AzureStorageConnectionString empty"

```bash
# Verificar se .env foi lido
docker run --env-file .env --env-file /dev/null -it nextdns-importer:latest env | grep ListImport
```

### ACI mostra "Failed" status

```bash
# Ver logs detalhados
az container logs --resource-group seu-rg --name nextdns-importer --follow

# Ver eventos
az container show --resource-group seu-rg --name nextdns-importer --query instanceView
```

### Secret não está sendo injetada no ACI

✅ Verifique se está usando `${{ secrets.AZURE_STORAGE_CONNECTION_STRING }}` no workflow
✅ Verifique se a secret existe no repositório (Settings → Secrets)

---

## 💡 Dicas

- **Local**: Use `.env` com sua connection string real
- **Staging**: Use environment staging no workflow
- **Production**: Use environment production e review antes de deployal
- **Logs**: Configure Log Level em `appsettings.json` para mais detalhe


# 🐳 Build Local - Passo a Passo

## ✅ Pré-requisitos

```bash
docker --version     # Docker 27.0.3 ou superior
az --version         # Azure CLI (para push)
```

## 🚀 Executar Build Local

### Opção 1: Usando Script Windows (Recomendado)

```cmd
cd C:\Users\herna\source\repos\DnsBlocker
scripts\build-importer.bat build
```

### Opção 2: Comando Docker Direto

```bash
cd C:\Users\herna\source\repos\DnsBlocker

docker build \
    -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" \
    -t "importer:latest" \
    -t "importer:v1.0.0" \
    .
```

### Opção 3: PowerShell

```powershell
$ProjectPath = "C:\Users\herna\source\repos\DnsBlocker"
Set-Location $ProjectPath

docker build `
    -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" `
    -t "importer:latest" `
    -t "importer:v1.0.0" `
    .
```

## 📊 O que vai acontecer

```
1. Build Stage (SDK image)
   ├─ Instancia container com .NET SDK
   ├─ Copia arquivos .csproj
   ├─ Executa: dotnet restore
   ├─ Copia código fonte
   ├─ Executa: dotnet publish -c Release
   └─ Resultado: /app/publish (compilado)

2. Runtime Stage (Runtime image)
   ├─ Instancia container com .NET Runtime
   ├─ Copia /app/publish do builder
   ├─ Copia appsettings.json
   └─ ENTRYPOINT: dotnet app.dll

Total: ~8-12 minutos (primeira vez)
Tamanho final: ~200-250 MB
```

## ✅ Validar Build

### Verificar Imagens Criadas

```bash
docker images | grep importer
```

**Esperado:**
```
importer         latest      abcd1234efgh   2 minutes ago   250MB
importer         v1.0.0      abcd1234efgh   2 minutes ago   250MB
```

### Testar Container Localmente

```bash
docker run --rm importer:latest
```

**Esperado (vai falhar, mas isso é normal):**
```
═══════════════════════════════════════
   NextDnsBetBlocker Import Worker
   Running in ACI (Azure Container)
═══════════════════════════════════════

[ERROR] Failed to initialize storage infrastructure
...Azure.Data.Tables.TableServiceClient...
...Connection string is invalid...
```

**Isso é esperado porque:**
- ✅ Container rodou (não erro de Docker)
- ✅ Aplicação iniciou (.NET executou)
- ❌ Falhou em storage (porque precisa Azure)

## 🔍 Verificar Logs da Build

Se der erro, check:

```bash
# Ver logs de build com mais detalhes
docker build \
    -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" \
    -t "importer:latest" \
    --progress=plain \
    .

# Ou com colors
docker build \
    -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" \
    -t "importer:latest" \
    --progress=tty \
    .
```

## 🐛 Troubleshooting

### Erro: "Cannot connect to Docker daemon"

```bash
# Iniciar Docker
# Windows: Abrir Docker Desktop
# Linux: sudo systemctl start docker
# macOS: Docker já roda em background
```

### Erro: "failed to solve: process did not complete successfully"

```bash
# Limpar cache do Docker
docker builder prune -a

# Tentar build novamente
docker build -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" -t "importer:latest" .
```

### Erro: "Access denied... appsettings.json"

```bash
# Verificar se arquivo existe
ls -la "src/NextDnsBetBlocker.Worker.Importer/appsettings.json"

# Se não existir, criar um vazio
touch "src/NextDnsBetBlocker.Worker.Importer/appsettings.json"
```

## 📈 Próximos Passos (Após Build OK)

### 1. Push para ACR

```bash
# Login no Azure
az login

# Login no ACR
az acr login --name myacr

# Tag para ACR
docker tag importer:latest myacr.azurecr.io/importer:v1.0.0

# Push
docker push myacr.azurecr.io/importer:v1.0.0
```

### 2. Deploy em ACI (Teste Manual)

```bash
az container create \
  --resource-group mygroup \
  --name importer-test-1 \
  --image myacr.azurecr.io/importer:v1.0.0 \
  --registry-login-server myacr.azurecr.io \
  --registry-username <username> \
  --registry-password <password> \
  --cpu 1 \
  --memory 1 \
  --restart-policy Never \
  --environment-variables \
    ASPNETCORE_ENVIRONMENT=Production \
    "AzureStorageConnectionString=DefaultEndpointsProtocol=https;AccountName=...;..."
```

### 3. Verificar Logs em ACI

```bash
az container logs \
  --resource-group mygroup \
  --name importer-test-1 \
  --follow
```

## ✅ Checklist

- [ ] Docker instalado e rodando
- [ ] Build completou com sucesso
- [ ] Imagens criadas localmente
- [ ] Container roda (mesmo com erro de storage)
- [ ] Pronto para push em ACR

---

**Status:** 🚀 Pronto para deploy!

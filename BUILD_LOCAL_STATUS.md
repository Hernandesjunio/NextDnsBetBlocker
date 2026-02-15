# ✅ BUILD LOCAL - STATUS & PRÓXIMOS PASSOS

## 🎯 Problema Identificado

O terminal não está conseguindo executar Docker via linha de comando. Isso é comum em ambientes com Docker Desktop.

**Solução:** Usar Docker Desktop GUI ou PowerShell

---

## 🚀 ALTERNATIVA 1: Docker Desktop (Recomendado)

### Abrir Docker Desktop

1. **Windows Start Menu** → "Docker Desktop"
2. Aguardar inicializar (ícone deve ficar verde)
3. Verificar: `docker --version` deve retornar versão

### Build via PowerShell

```powershell
# Abrir PowerShell como Admin
cd "C:\Users\herna\source\repos\DnsBlocker"

# Build image
docker build `
    -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" `
    -t "importer:latest" `
    -t "importer:v1.0.0" `
    .

# Resultado esperado
# DOCKER_BUILDKIT=1 docker build (automático no Docker Desktop)
# ... diversos passos ...
# => => naming to docker.io/library/importer:latest
```

### Testar Container

```powershell
docker run --rm importer:latest
```

---

## 🚀 ALTERNATIVA 2: Usar Script Batch Diretamente

```cmd
cd C:\Users\herna\source\repos\DnsBlocker
scripts\build-importer.bat build
```

---

## 📊 O QUE VAI ACONTECER DURANTE BUILD

```
Step 1: FROM mcr.microsoft.com/dotnet/sdk:10.0
  → Download imagem SDK (~1-2 min, primeira vez)

Step 2: COPY project files
  → Copia .csproj

Step 3: RUN dotnet restore
  → Restaura dependências NuGet (~2-3 min)

Step 4: COPY source code
  → Copia código

Step 5: RUN dotnet publish
  → Compila e publica (~3-5 min)

Step 6: FROM mcr.microsoft.com/dotnet/runtime:10.0
  → Download runtime image

Step 7: COPY published files
  → Copia binários do builder

Step 8: ENTRYPOINT
  → Define como executar

RESULTADO: importer:latest (~200-250 MB)
TEMPO TOTAL: 8-12 minutos (primeira vez)
```

---

## ✅ VALIDAÇÃO PÓS-BUILD

Depois que build completar com sucesso:

```powershell
# 1. Verificar imagem criada
docker images | Select-String importer

# Esperado output:
# importer         latest      abc123def456   5 minutes ago   250MB

# 2. Testar container
docker run --rm importer:latest

# Esperado: Container vai rodar e depois falhar em Storage (normal!)
# [ERROR] Failed to initialize storage infrastructure
```

---

## 📋 BUILD CHECKLIST

- [ ] Docker Desktop aberto e rodando (ícone verde)
- [ ] PowerShell ou Cmd aberto como Admin
- [ ] Navegou até: `C:\Users\herna\source\repos\DnsBlocker`
- [ ] Executou: `docker build -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" -t "importer:latest" .`
- [ ] Build completou com sucesso (sem erros)
- [ ] Imagem criada: `docker images | grep importer`
- [ ] Container testado: `docker run --rm importer:latest`

---

## 🎯 PRÓXIMOS PASSOS (APÓS BUILD OK)

### Passo 1: Criar ACR (Se não existir)

```powershell
# Criar Azure Container Registry
az acr create `
  --resource-group mygroup `
  --name myacr `
  --sku Basic
```

### Passo 2: Login no ACR

```powershell
az acr login --name myacr
```

### Passo 3: Tag para ACR

```powershell
docker tag importer:latest myacr.azurecr.io/importer:v1.0.0
```

### Passo 4: Push para ACR

```powershell
docker push myacr.azurecr.io/importer:v1.0.0
```

### Passo 5: Verify em ACR

```powershell
az acr repository list --name myacr
```

---

## 🐛 TROUBLESHOOTING

### "Docker daemon is not running"

```powershell
# Abrir Docker Desktop e aguardar inicializar
# Depois verificar
docker --version
```

### "Access denied"

```powershell
# Executar PowerShell como Admin
# Clique direito: Run as Administrator
```

### "failed to solve"

```powershell
# Limpar cache Docker
docker builder prune -a

# Tentar build novamente
docker build -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" -t "importer:latest" .
```

### "No space left on device"

```powershell
# Limpar Docker
docker system prune -a

# Verifica espaço em disco
dir C:\  # Deve ter >10GB livres
```

---

## 📊 STATUS ATUAL

```
✅ Código: Pronto
✅ Dockerfile: Pronto
✅ Scripts: Pronto
✅ Documentação: Completa

⏳ Build: Aguardando execução
```

---

## 🎬 COMECE AGORA!

### OPÇÃO A: PowerShell (Recomendado)

```powershell
# 1. Abra PowerShell como Admin
# 2. Execute:
cd "C:\Users\herna\source\repos\DnsBlocker"
docker build -f "src/NextDnsBetBlocker.Worker.Importer/Dockerfile" -t "importer:latest" .

# 3. Aguarde 8-12 minutos
# 4. Teste:
docker run --rm importer:latest
```

### OPÇÃO B: CMD/Batch

```cmd
cd C:\Users\herna\source\repos\DnsBlocker
scripts\build-importer.bat build
```

### OPÇÃO C: Docker Desktop GUI

1. Abra Docker Desktop
2. Vá para "Containers"
3. Clique em "Build" (se disponível)

---

## 📞 PRÓXIMO PASSO

**Quando build completar, volte aqui e me manda a saída do:**

```powershell
docker images | grep importer
```

---

**Hora de começar! 🚀**

Abra Docker Desktop e execute o build!

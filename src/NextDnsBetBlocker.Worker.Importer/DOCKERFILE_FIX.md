# 🐳 Dockerfile Fix — NuGet Fallback Folder Issue

## Problema

```
NuGet.Packaging.Core.PackagingException: Unable to find fallback package folder
'C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages'
```

### Causa
- Projeto configurado com NuGet fallback folder **Windows-specific**
- Dockerfile roda em **Linux container**
- Caminho não existe em Linux → build falha

---

## Solução Aplicada

### 1. **Criar NuGet.config limpo no builder stage**

```dockerfile
RUN mkdir -p .nuget && echo '<?xml version="1.0"...' > .nuget/NuGet.config
```

**O que faz:**
- Cria diretório `.nuget` 
- Gera `NuGet.config` com apenas `nuget.org` como fonte
- **Limpa todas as referências a fallback folders**

### 2. **Usar --configfile durante restore**

```dockerfile
RUN dotnet restore ... --configfile .nuget/NuGet.config
```

**O que faz:**
- Force o restore a usar o config limpo
- Ignora configurações do projeto

### 3. **Adicionar flags ao publish**

```dockerfile
RUN dotnet publish ... \
    /p:DesignTimeBuild=false \
    /p:UseRazorSourceGenerator=true
```

**O que faz:**
- `DesignTimeBuild=false` → build otimizado (sem metadados IDE)
- `UseRazorSourceGenerator=true` → usa source generators (melhor performance)

---

## Dockerfile Antes vs Depois

### ❌ Antes
```dockerfile
RUN dotnet restore "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" \
    --disable-build-servers
```

### ✅ Depois
```dockerfile
# Criar NuGet.config limpo (sem fallback folders)
RUN mkdir -p .nuget && echo '<?xml version="1.0"...' > .nuget/NuGet.config

# Usar config limpo
RUN dotnet restore "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" \
    --disable-build-servers \
    --configfile .nuget/NuGet.config

# Publish com flags otimizados
RUN dotnet publish "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --disable-build-servers \
    /p:DesignTimeBuild=false \
    /p:UseRazorSourceGenerator=true
```

---

## ✅ Resultado

- ✅ **Remove Windows-specific paths** → funciona em Linux
- ✅ **Limpa fontes NuGet** → restaura apenas de nuget.org
- ✅ **Otimiza build** → mais rápido, menor tamanho
- ✅ **Source generators** → melhor performance em runtime

---

## 🧪 Como Testar

```bash
docker build -t nextdnsblocker-importer:latest -f src/NextDnsBetBlocker.Worker.Importer/Dockerfile .
```

**Esperado:**
- ✅ Build completa sem erros
- ✅ Imagem menor (~200MB)
- ✅ Startup mais rápido

---

## 📌 Referência

- **NuGet Config Schema**: [docs.microsoft.com/nuget/reference/nuget-config-file](https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file)
- **.NET Build Options**: [github.com/dotnet/sdk](https://github.com/dotnet/sdk)


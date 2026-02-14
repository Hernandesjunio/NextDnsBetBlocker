# 🚀 SEPARAÇÃO DE WORKERS - IMPLEMENTAÇÃO COMPLETA

## 📊 Arquitetura Final

```
TIER 1: LOCAL MACHINE (Bare Metal)
═════════════════════════════════════
NextDnsBetBlocker.Worker.Importer
├─ ImportListBackgroundService
│  ├─ Initial: 5M Tranco domains
│  └─ Weekly: Diff imports (~1k ops)
├─ StorageInfrastructureInitializer
│  └─ Cria/valida tabelas
├─ TrancoAllowlistProvider
└─ ListTableProvider (cache)
   └─ Queries Table Storage

       ↓ (PERSISTS)

Table Storage (Shared)
├─ TrancoList (4M)
├─ BlockedDomains
├─ AgentState
└─ GamblingSuspects

       ↓ (EVENT STREAM)

Queue Storage
└─ suspicious-domains

   ─────────────────────────────────────

TIER 2: AZURE CLOUD
═════════════════════════════════════
NextDnsBetBlocker.Worker (Analysis/Blocking)
├─ BetBlockerPipeline
├─ ClassifierConsumer
│  └─ Publica na fila
├─ AnalysisConsumer
├─ SuspectDomainQueuePublisher
└─ Listening to queue

       ↓ (ON-DEMAND)

Azure Functions
├─ AnalyzeDomainFunction
└─ BlockDomainFunction
```

---

## 📁 Novo Projeto Criado

### **NextDnsBetBlocker.Worker.Importer**

```
src\NextDnsBetBlocker.Worker.Importer\
├── NextDnsBetBlocker.Worker.Importer.csproj
├── Program.cs (só importação)
├── appsettings.json
└── appsettings.Development.json
```

### **Modificações em NextDnsBetBlocker.Worker**

- ✅ Remover `ImportListBackgroundService` (move para Importer)
- ✅ Adicionar `ISuspectDomainQueuePublisher` (consome)
- ✅ Modificar `ClassifierConsumer` (publica na fila)
- ✅ Adicionar queue listening (opcional)

---

## 🔄 Responsabilidades

### **Worker.Importer (LOCAL)**
```csharp
✓ ImportListBackgroundService
  ├─ Download Tranco List
  ├─ Parse CSV/ZIP
  ├─ Batch insert Table Storage
  └─ Weekly diffs

✓ StorageInfrastructureInitializer
  └─ Cria tabelas

✓ TrancoAllowlistProvider
  └─ Cache + queries

✓ Runs 24/7 locally
✓ Low cost (apenas storage)
```

### **Worker (REMOTE/Azure)**
```csharp
✓ BetBlockerPipeline
  ├─ Fetch NextDNS logs
  ├─ Classify domains
  └─ Publish suspicious to queue

✓ ClassifierConsumer
  └─ Publica em ISuspectDomainQueuePublisher

✓ AnalysisConsumer
  └─ Publica em ISuspectDomainQueuePublisher

✓ Can run on App Service
✓ Can run on Container
✓ Can run on Azure Functions
```

---

## 🛠️ SETUP PASSO-A-PASSO

### PASSO 1: Build Nova Solução

```bash
cd C:\Users\herna\source\repos\DnsBlocker

# Adicionar novo projeto à solução
dotnet sln add src\NextDnsBetBlocker.Worker.Importer\NextDnsBetBlocker.Worker.Importer.csproj

# Build
dotnet build

# Testar
dotnet run --project src\NextDnsBetBlocker.Worker.Importer
```

### PASSO 2: Configurar Storage

**appsettings.json (ambos workers)**:
```json
{
  "AzureStorageConnectionString": "DefaultEndpointsProtocol=https;AccountName=dnsblockerstorage;..."
}
```

### PASSO 3: Testar Localmente (Importer)

**Terminal 1: Azurite (emulator)**
```bash
azurite --silent
```

**Terminal 2: Worker.Importer**
```bash
cd src\NextDnsBetBlocker.Worker.Importer
dotnet run
```

**Esperado**:
```
[INF] Initializing storage infrastructure for Importer...
[INF] ✓ Table initialized: AgentState
[INF] ✓ Table initialized: BlockedDomains
[INF] ✓ Table initialized: GamblingSuspects
[INF] ✓ Table initialized: TrancoList
[INF] Storage infrastructure initialized successfully
[INF] Starting ImportListBackgroundService
[INF] Performing initial import for TrancoList
[INF] Downloaded 4000000 domains from Tranco List
[INF] Initial import completed: 4000000 items inserted
```

### PASSO 4: Deploy Worker.Importer (LOCAL)

**Opção A: Executável Windows**
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

**Opção B: Docker (local)**
```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:10

COPY bin/Release/net10 /app

WORKDIR /app

ENTRYPOINT ["dotnet", "NextDnsBetBlocker.Worker.Importer.dll"]
```

**Opção C: Windows Service**
```bash
sc create NextDnsBetBlockerImporter binPath= "C:\path\to\NextDnsBetBlocker.Worker.Importer.exe"
sc start NextDnsBetBlockerImporter
```

---

## 📝 Modificações em NextDnsBetBlocker.Worker

### Remover ImportListBackgroundService

**Em Program.cs - REMOVER**:
```csharp
// ❌ REMOVER:
services.AddHostedService<ImportListBackgroundService>();
services.AddSingleton<TrancoListImporter>();
services.AddSingleton<ITrancoAllowlistProvider, TrancoAllowlistProvider>();
```

### Adicionar Queue Publisher

**Em Program.cs - ADICIONAR**:
```csharp
// ✅ ADICIONAR:
services.AddSingleton<ISuspectDomainQueuePublisher>(sp =>
{
    var connString = settings.AzureStorageConnectionString;
    return new SuspectDomainQueuePublisher(
        connString,
        sp.GetRequiredService<ILogger<SuspectDomainQueuePublisher>>());
});
```

### Modificar ClassifierConsumer

**Injetar publisher e publicar**:
```csharp
public class ClassifierConsumer : IClassifierConsumer
{
    private readonly ISuspectDomainQueuePublisher _queuePublisher;

    // ... constructor ...

    public async Task StartAsync(...)
    {
        await foreach (var suspect in inputChannel...)
        {
            // ... classificação ...
            
            var queueMessage = new SuspectDomainQueueMessage
            {
                Domain = suspect.Domain,
                ProfileId = profileId,
                FirstSeen = suspect.FirstSeen,
                ClassificationScore = classificationScore
            };

            await _queuePublisher.PublishAsync(queueMessage, cancellationToken);
        }
    }
}
```

---

## 🚀 DEPLOY EM PRODUÇÃO

### Local Machine
```bash
# Build Release
dotnet publish -c Release src\NextDnsBetBlocker.Worker.Importer

# Executar como serviço Windows
# ou Docker em máquina local
# ou scheduled task
```

### Azure App Service (Worker Analysis)
```bash
# Deploy Worker (analysis/blocking)
az webapp deployment source config-zip \
  --resource-group dns-blocker \
  --name dns-blocker-analysis \
  --src release.zip
```

### Azure Functions (Optional)
```bash
# Deploy Functions (analysis/blocking via queue)
func azure functionapp publish dns-blocker-functions
```

---

## 📊 FLUXO DE DADOS

```
[LOCAL MACHINE]
    ↓
ImportListBackgroundService
    ├─ Lê 5M do Tranco
    └─ Escreve em Table Storage
    
    ↓ (uma vez)

[AZURE - Worker Analysis]
    ↓
BetBlockerPipeline
    ├─ Fetch NextDNS logs
    ├─ Classify com Tranco (local cache)
    └─ Suspicious → Queue
    
    ↓

[Azure Queue]
    ├─ suspicious-domains

    ↓ (triggered)

[Azure Functions]
    ├─ AnalyzeDomainFunction
    ├─ BlockDomainFunction
    └─ NextDNS API calls
```

---

## 🔒 Security Notes

```
✅ Connection strings em Key Vault
✅ Worker.Importer: read TrancoList only
✅ Worker Analysis: read logs, publish queue
✅ Functions: read queue, write NextDNS
✅ Network: VNet para privacidade
```

---

## 💰 Custo Total

```
Local Machine:
├─ Electricity: ~$5/mês
└─ Your machine: Already owned

Azure Storage:
├─ Table Storage: ~$1/mês
├─ Queue Storage: ~$0.01/mês
└─ Blob Storage: ~$1/mês

Azure Functions:
└─ $0.20/1M execs ≈ $0.40/mês

TOTAL: ~$8-10/mês ✅
SAVINGS: -80% vs always-on cloud
```

---

## ✅ Checklist

```
Implementação:
☐ Novo projeto .Importer criado
☐ Program.cs Importer configurado
☐ appsettings.json Importer criado
☐ NextDnsBetBlocker.Worker modificado
☐ ImportListBackgroundService removido
☐ Queue publisher adicionado
☐ ClassifierConsumer modificado

Testing:
☐ Build ambos projetos
☐ Testar Worker.Importer localmente
☐ Testar Worker em container
☐ Validar fluxo da fila
☐ End-to-end com Functions

Deployment:
☐ Importer rodando localmente
☐ Worker em Azure App Service
☐ Functions em Azure
☐ Monitoring habilitado
☐ Alerts configurados
```

---

**Status**: ✅ PRONTO PARA DEPLOY
**Arquitetura**: Separada e otimizada
**Custo**: ~$8-10/mês ✅

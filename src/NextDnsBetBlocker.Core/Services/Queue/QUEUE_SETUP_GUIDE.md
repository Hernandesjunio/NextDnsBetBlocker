# 🚀 ARQUITETURA DE CUSTO MÍNIMO - SETUP COMPLETO

## 📊 Arquitetura

```
LOCAL MACHINE (bare metal - seu computador)
│
├─ ImportListBackgroundService
│  ├─ Importação inicial: 5M domínios
│  └─ Diffs periódicos: 1x/semana
│
├─ Table Storage (compartilhado)
│  ├─ TrancoList (4M)
│  ├─ BlockedDomains
│  └─ AgentState
│
└─ Storage Queue Publisher
   └─ Publica suspicious domains

       ↓ Muito barato: ~$0.0001/1M ops

AZURE STORAGE QUEUE
└─ suspicious-domains (fila de entrada)

       ↓ Triggered (pay-per-use)

AZURE FUNCTIONS (Consumption Plan)
├─ AnalyzeDomainFunction
│  ├─ Consome da fila
│  ├─ Análise de reputação
│  └─ Publica para domains-ready-to-block
│
└─ BlockDomainFunction
   ├─ Consome de domains-ready-to-block
   ├─ Bloqueia no NextDNS
   └─ Registra resultado

CUSTO TOTAL: ~$2-5/mês
```

---

## 📦 Componentes Criados

### 1. **SuspectDomainQueueMessage.cs**
Modelo de mensagem para a fila

### 2. **ISuspectDomainQueuePublisher.cs**
Interface para publicação

### 3. **SuspectDomainQueuePublisher.cs**
Implementação (Azure Storage Queue)

### 4. **AnalyzeDomainFunction.example.cs**
Template de função de análise

### 5. **BlockDomainFunction.example.cs**
Template de função de bloqueio

---

## 🔧 SETUP PASSO-A-PASSO

### Passo 1: Storage Account Azure

```bash
# Criar storage account
az storage account create \
  --name dnsblockerstorage \
  --resource-group dns-blocker \
  --location eastus \
  --sku Standard_LRS

# Pegar connection string
az storage account show-connection-string \
  --name dnsblockerstorage \
  --resource-group dns-blocker
```

Copiar: `DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net`

### Passo 2: Registrar em Program.cs (Local Worker)

**Add NuGet**:
```bash
dotnet add package Azure.Storage.Queues
```

**Em ConfigureServices (Program.cs)**:
```csharp
// Queue Publisher para domínios suspeitos
services.AddSingleton<ISuspectDomainQueuePublisher>(sp =>
{
    var connectionString = settings.AzureStorageConnectionString;
    return new SuspectDomainQueuePublisher(
        connectionString,
        sp.GetRequiredService<ILogger<SuspectDomainQueuePublisher>>());
});
```

**Em appsettings.json**:
```json
{
  "AzureStorageConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
  "ListImport": {
    "TrancoList": {
      "Enabled": true,
      "SourceUrl": "https://tranco-list.eu/top-1m.csv.zip",
      "Table": "TrancoList",
      "BlobContainer": "tranco-lists"
    }
  }
}
```

### Passo 3: Modificar BetBlockerPipeline (Local)

Ao invés de bloquear direto, publicar na fila:

**Encontre em `BetBlockerPipeline.cs` ou `AnalysisConsumer.cs`**:

```csharp
// ANTES: Bloquear direto
await _nextDnsClient.AddToDenylistAsync(profileId, domain);

// DEPOIS: Publicar para fila
var queueMessage = new SuspectDomainQueueMessage
{
    Domain = domain,
    ProfileId = profileId,
    FirstSeen = DateTime.UtcNow,
    ClassificationScore = 0.95
};

await _queuePublisher.PublishAsync(queueMessage);
```

### Passo 4: Criar Azure Functions

**Opção A: Visual Studio**
```bash
dotnet new azurefunctions -n NextDnsBetBlocker.Functions
cd NextDnsBetBlocker.Functions

# Add templates
dotnet new queuetrigger -n AnalyzeDomain
dotnet new queuetrigger -n BlockDomain
```

**Opção B: Azure Portal**
```
1. Create Function App
2. Runtime: .NET 8 (Isolated)
3. Hosting: Consumption Plan
4. Create function: Queue trigger
5. Queue name: suspicious-domains
```

### Passo 5: Implementar AnalyzeDomainFunction

Usar template de `AnalyzeDomainFunction.example.cs`:

```csharp
[Function("AnalyzeDomain")]
public async Task Run(
    [QueueTrigger("suspicious-domains")] 
    SuspectDomainQueueMessage suspect,
    [Queue("domains-ready-to-block")] 
    IAsyncCollector<DomainBlockRequest> blockQueue,
    FunctionContext context)
{
    // 1. Análise
    var score = await AnalyzeDomain(suspect.Domain);
    
    // 2. Se confiante, enviar para bloqueio
    if (score > 0.8)
    {
        await blockQueue.AddAsync(new DomainBlockRequest
        {
            Domain = suspect.Domain,
            ProfileId = suspect.ProfileId,
            ConfidenceScore = score
        });
    }
}

private async Task<double> AnalyzeDomain(string domain)
{
    // TODO: Implementar análise real
    // - HTTP requests para serviços de reputação
    // - ML classification
    // - Database lookups
    
    return 0.85; // Placeholder
}
```

### Passo 6: Implementar BlockDomainFunction

```csharp
[Function("BlockDomain")]
public async Task Run(
    [QueueTrigger("domains-ready-to-block")] 
    DomainBlockRequest blockRequest,
    FunctionContext context)
{
    var logger = context.GetLogger("BlockDomain");
    
    try
    {
        // Obter INextDnsClient injetado
        var serviceProvider = context.GetServiceProvider();
        var nextDnsClient = serviceProvider.GetRequiredService<INextDnsClient>();
        
        // Bloquear
        var success = await nextDnsClient.AddToDenylistAsync(
            blockRequest.ProfileId,
            new DenylistBlockRequest { Domain = blockRequest.Domain });
        
        if (success)
        {
            logger.LogInformation("Domain blocked: {Domain}", blockRequest.Domain);
            // Registrar em BlockedDomainStore
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error blocking domain: {Domain}", blockRequest.Domain);
        throw; // Retry
    }
}
```

### Passo 7: Configurar Storage Accounts

**Connection String para Functions**:

Ir para `Configuration` → `Application settings`:
- `AzureWebJobsStorage`: (preenchida automaticamente)
- `FUNCTIONS_WORKER_RUNTIME`: `dotnet-isolated`

**Local appsettings.json**:
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=...",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
  }
}
```

---

## 🧪 Testando Localmente

### 1. Azure Storage Emulator (Azurite)

```bash
# Instalar
npm install -g azurite

# Rodar emulator
azurite --silent --location ./data

# Connection string
DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;
```

### 2. Testar Publisher

```csharp
[Test]
public async Task TestPublishDomain()
{
    var publisher = new SuspectDomainQueuePublisher(
        "DefaultEndpointsProtocol=http;...",
        new NullLogger<SuspectDomainQueuePublisher>());
    
    var message = new SuspectDomainQueueMessage
    {
        Domain = "example.com",
        ProfileId = "test-profile",
        FirstSeen = DateTime.UtcNow
    };
    
    await publisher.PublishAsync(message);
    // Verificar em Azure Storage Explorer
}
```

### 3. Testar Função Localmente

```bash
cd NextDnsBetBlocker.Functions

# Start function runtime
func start

# Em outro terminal, publicar mensagem
az storage message put \
  --queue-name suspicious-domains \
  --content '{"domain":"example.com","profileId":"test"}'
```

---

## 📊 Monitoramento

### Application Insights

```bash
# Criar
az resource create \
  --resource-group dns-blocker \
  --resource-type "Microsoft.Insights/components" \
  --name dns-blocker-insights \
  --properties '{"Application_Type":"web"}'
```

### Logs em Functions

```csharp
public async Task Run(
    [QueueTrigger("suspicious-domains")] string message,
    ILogger log)
{
    log.LogInformation($"Processing message: {message}");
}
```

---

## 💰 Custo Estimado

```
Storage Account:
├─ Table Storage: ~$1/mês
├─ Queue Storage: ~$0.01/mês
└─ Blob Storage: ~$1/mês

Azure Functions:
├─ Execuções: ~100k/mês = $0
├─ Premium para retenção: ~$0
└─ Total: ~$0.20/mês

Total: ~$2-3/mês ✅
```

---

## 🚀 Checklist

```
Local Setup:
☐ Criar Storage Account
☐ Add NuGet Azure.Storage.Queues
☐ Registrar ISuspectDomainQueuePublisher
☐ Testar conexão com Publisher

Azure Setup:
☐ Criar Function App
☐ Criar AnalyzeDomainFunction
☐ Criar BlockDomainFunction
☐ Configurar storage connection strings
☐ Deploy functions

Testing:
☐ Testar publish localmente
☐ Testar consumo em functions
☐ Validar bloqueios no NextDNS
☐ Verificar logs

Production:
☐ Enable Application Insights
☐ Configure alerts
☐ Monitor costs
☐ Set up auto-scaling (if needed)
```

---

## 📝 Próximos Passos

1. Criar Storage Account
2. Adicionar connection string em appsettings
3. Registrar publisher em DI
4. Criar Azure Function App
5. Implementar funções baseado em templates
6. Deploy e testar

---

**Custo final**: ~$2-5/mês vs ~$30+ com solução tradicional ✅

# 📋 INTEGRAÇÃO EM PROGRAM.CS

## Adicionar Queue Publisher em ConfigureServices

```csharp
// ============= QUEUE PUBLISHER FOR ANALYSIS FUNCTIONS =============

// Register Suspect Domain Queue Publisher
services.AddSingleton<ISuspectDomainQueuePublisher>(sp =>
{
    var connectionString = settings.AzureStorageConnectionString;
    
    // Validar connection string
    if (string.IsNullOrEmpty(connectionString))
    {
        var logger = sp.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("AzureStorageConnectionString not configured - Queue publisher will fail");
    }
    
    return new SuspectDomainQueuePublisher(
        connectionString ?? "DefaultEndpointsProtocol=https;",
        sp.GetRequiredService<ILogger<SuspectDomainQueuePublisher>>());
});

// ============= END QUEUE PUBLISHER =============
```

## Modificar ClassifierConsumer ou AnalysisConsumer

### Opção 1: ClassifierConsumer

```csharp
public class ClassifierConsumer : IClassifierConsumer
{
    private readonly IBetClassifier _classifier;
    private readonly IHageziProvider _hageziProvider;
    private readonly IAllowlistProvider _allowlist;
    private readonly ISuspectDomainQueuePublisher _queuePublisher;  // ← ADD
    private readonly ILogger<ClassifierConsumer> _logger;

    public ClassifierConsumer(
        IBetClassifier classifier,
        IHageziProvider hageziProvider,
        IAllowlistProvider allowlist,
        ISuspectDomainQueuePublisher queuePublisher,  // ← ADD
        ILogger<ClassifierConsumer> logger)
    {
        _classifier = classifier;
        _hageziProvider = hageziProvider;
        _allowlist = allowlist;
        _queuePublisher = queuePublisher;  // ← ADD
        _logger = logger;
    }

    public async Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        // ... existing code ...

        await foreach (var suspect in inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            // ... classification logic ...

            // ← ADD THIS:
            if (isClassifiedAsGambling)
            {
                var queueMessage = new SuspectDomainQueueMessage
                {
                    Domain = suspect.Domain,
                    ProfileId = profileId,
                    FirstSeen = suspect.FirstSeen,
                    ClassificationScore = classificationScore,
                    Source = "ClassifierConsumer",
                    CorrelationId = suspect.Domain.GetHashCode().ToString()
                };

                try
                {
                    await _queuePublisher.PublishAsync(queueMessage, cancellationToken);
                    _logger.LogInformation(
                        "Published domain for analysis: {Domain} (Score: {Score})",
                        suspect.Domain,
                        classificationScore);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish domain to queue: {Domain}", suspect.Domain);
                    // Decidir: falhar ou continuar?
                    // Opção 1: throw (vai falhar o pipeline)
                    // Opção 2: continue (perde o domínio)
                    throw; // Recomendado: falhar
                }
            }
            
            // Continuar com pipeline local também (ou remover se puro queue-driven)
            await outputChannel.Writer.WriteAsync(suspect, cancellationToken);
        }
    }
}
```

### Opção 2: AnalysisConsumer

```csharp
public class AnalysisConsumer : IAnalysisConsumer
{
    private readonly IGamblingSuspectAnalyzer _analyzer;
    private readonly ISuspectDomainQueuePublisher _queuePublisher;  // ← ADD
    private readonly ILogger<AnalysisConsumer> _logger;

    // ... constructor ...

    public async Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        await foreach (var suspect in inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            // Local analysis (optional - can skip this)
            var analysisResult = await _analyzer.AnalyzeDomainAsync(suspect.Domain);

            // ← ALWAYS publish to queue for cloud processing
            var queueMessage = new SuspectDomainQueueMessage
            {
                Domain = suspect.Domain,
                ProfileId = profileId,
                FirstSeen = suspect.FirstSeen,
                ClassificationScore = analysisResult?.Score ?? 0.5,
                Source = "AnalysisConsumer",
                CorrelationId = Guid.NewGuid().ToString()
            };

            try
            {
                await _queuePublisher.PublishAsync(queueMessage, cancellationToken);
                _logger.LogInformation("Published domain for cloud analysis: {Domain}", suspect.Domain);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish domain: {Domain}", suspect.Domain);
                throw;
            }
        }
    }
}
```

## Adicionar em appsettings.json

```json
{
  "AzureStorageConnectionString": "DefaultEndpointsProtocol=https;AccountName=dnsblockerstorage;AccountKey=YOUR_KEY==;EndpointSuffix=core.windows.net",
  
  "ListImport": {
    "TrancoList": {
      "Enabled": true,
      "SourceUrl": "https://tranco-list.eu/top-1m.csv.zip",
      "Table": "TrancoList",
      "BlobContainer": "tranco-lists",
      "BatchSize": 100,
      "MaxPartitions": 10,
      "ThrottleOperationsPerSecond": 150000,
      "ChannelCapacity": 10000
    }
  }
}
```

## Adicionar em appsettings.Development.json

```json
{
  "AzureStorageConnectionString": "UseDevelopmentStorage=true",
  
  "Logging": {
    "LogLevel": {
      "NextDnsBetBlocker": "Debug",
      "NextDnsBetBlocker.Core.Services.Queue": "Information"
    }
  }
}
```

---

## 🧪 Testar Localmente

### 1. Com Azurite

```bash
# Terminal 1: Rodar emulator
azurite --silent

# Terminal 2: Rodar aplicação
dotnet run
```

### 2. Com Account Real

Apenas substituir connection string em appsettings.json

### 3. Verificar Fila

Azure Storage Explorer:
```
Storage Accounts
└─ dnsblockerstorage
   └─ Queues
      └─ suspicious-domains (mensagens aqui)
```

---

## ✅ Checklist

```
☐ Add NuGet: Azure.Storage.Queues
☐ Add SuspectDomainQueuePublisher ao DI
☐ Injetar em ClassifierConsumer
☐ Adicionar connection string em appsettings
☐ Testar localmente com Azurite
☐ Validar mensagens na fila
☐ Deploy Azure Functions
☐ Testar com account real
```

---

**Status**: Pronto para integração! ✅

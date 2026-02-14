# 📋 MODIFICAR WORKER (ANALYSIS) - PRÓXIMAS AÇÕES

## O Que Fazer no NextDnsBetBlocker.Worker

Seu `NextDnsBetBlocker.Worker` atual roda TUDO (import + analysis).
Agora precisa rodar APENAS analysis (e publicar na fila).

---

## ✏️ PASSO 1: Modificar Program.cs

### REMOVER (Seção Import)

Localizar e REMOVER as seguintes linhas:

```csharp
// ❌ REMOVER: Import services registration
services.AddMemoryCache();
services.AddSingleton<IImportMetricsCollector, ImportMetricsCollector>();
services.AddSingleton<IPartitionKeyStrategy>(sp => new PartitionKeyStrategy(10));
services.AddSingleton<IImportRateLimiter>(sp => new ImportRateLimiter(150000));
services.AddHttpClient<IListImportProducer, ListImportProducer>();
services.AddSingleton<IListImportConsumer, ListImportConsumer>();
services.AddSingleton<IListImportOrchestrator, ListImportOrchestrator>();
services.AddSingleton<IListBlobRepository>(sp => ...);

// ❌ REMOVER: Import background service
services.AddHostedService<ImportListBackgroundService>();

// ❌ REMOVER: Tranco importer specific
services.AddSingleton<TrancoListImporter>();
var trancoConfig = TrancoListImporter.CreateConfig();
services.AddSingleton(trancoConfig);
```

### ADICIONAR (Queue Publisher)

Encontrar onde se registra os services e ADICIONAR:

```csharp
// ✅ ADICIONAR: Queue Publisher para analysis
services.AddSingleton<ISuspectDomainQueuePublisher>(sp =>
{
    var connectionString = settings.AzureStorageConnectionString;
    return new SuspectDomainQueuePublisher(
        connectionString,
        sp.GetRequiredService<ILogger<SuspectDomainQueuePublisher>>());
});
```

---

## ✏️ PASSO 2: Modificar ClassifierConsumer

### INJETAR Publisher

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
}
```

### PUBLICAR NA FILA

Localizar onde se escreve ao outputChannel e ADICIONAR publicação na fila:

```csharp
public async Task StartAsync(
    Channel<SuspectDomainEntry> inputChannel,
    Channel<SuspectDomainEntry> outputChannel,
    string profileId,
    CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("ClassifierConsumer started for profile {ProfileId}", profileId);

        int processed = 0;
        int filtered = 0;
        int suspectCount = 0;

        // Read all logs from input channel
        await foreach (var logEntry in inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            var domain = logEntry.Domain.ToLowerInvariant();

            // Check allowlist
            if (_allowlist.GetAllowlist().Contains(domain))
            {
                filtered++;
                continue;
            }

            // Check HaGeZi
            var gamblingDomains = await _hageziProvider.GetGamblingDomainsAsync();
            if (gamblingDomains.Contains(domain))
            {
                filtered++;
                continue;
            }

            // Classify with BetClassifier
            if (!_classifier.IsBetDomain(domain))
            {
                filtered++;
                continue;
            }

            // Found suspicious domain
            suspectCount++;

            var suspectEntry = new SuspectDomainEntry
            {
                Domain = domain,
                FirstSeen = logEntry.Timestamp,
                ProfileId = profileId,
                ClassificationScore = 0.8 // Default score
            };

            // ✅ PUBLICAR NA FILA (NEW)
            var queueMessage = new SuspectDomainQueueMessage
            {
                Domain = domain,
                ProfileId = profileId,
                FirstSeen = logEntry.Timestamp,
                ClassificationScore = 0.8,
                Source = "ClassifierConsumer",
                CorrelationId = Guid.NewGuid().ToString()
            };

            try
            {
                await _queuePublisher.PublishAsync(queueMessage, cancellationToken);
                _logger.LogDebug(
                    "Published suspicious domain to queue: {Domain} (CorrelationId: {CorrelationId})",
                    domain,
                    queueMessage.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish domain {Domain} to queue", domain);
                // Decidir: falhar ou continuar?
                // throw; // Opção 1: Falhar
                // continue; // Opção 2: Continuar (pode perder domínio)
                throw; // Recomendado
            }

            // Continuar pipeline local também (ou remover se puro queue-driven)
            await outputChannel.Writer.WriteAsync(suspectEntry, cancellationToken);

            if (processed % 1000 == 0)
                _logger.LogDebug(
                    "Processed {Total} logs, filtered: {Filtered}, suspect: {Suspect}",
                    processed, filtered, suspectCount);
        }

        _logger.LogInformation(
            "ClassifierConsumer completed: Processed={Processed}, Filtered={Filtered}, Suspect={Suspect}",
            processed, filtered, suspectCount);
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("ClassifierConsumer cancelled");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "ClassifierConsumer failed");
        throw;
    }
    finally
    {
        // Signal completion to next consumer
        outputChannel.Writer.TryComplete();
    }
}
```

---

## ✏️ PASSO 3: Modificar AnalysisConsumer (OPCIONAL)

Se quiser análise local ANTES de enviar para fila:

```csharp
public class AnalysisConsumer : IAnalysisConsumer
{
    private readonly IGamblingSuspectAnalyzer _analyzer;
    private readonly ISuspectDomainQueuePublisher _queuePublisher;  // ← ADD
    private readonly ILogger<AnalysisConsumer> _logger;

    public AnalysisConsumer(
        IGamblingSuspectAnalyzer analyzer,
        ISuspectDomainQueuePublisher queuePublisher,  // ← ADD
        ILogger<AnalysisConsumer> logger)
    {
        _analyzer = analyzer;
        _queuePublisher = queuePublisher;
        _logger = logger;
    }

    public async Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("AnalysisConsumer started for profile {ProfileId}", profileId);

            await foreach (var suspect in inputChannel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Análise local (opcional)
                var analysisResult = await _analyzer.AnalyzeDomainAsync(suspect.Domain);

                // ✅ PUBLICAR NA FILA para análise/bloqueio remoto
                var queueMessage = new SuspectDomainQueueMessage
                {
                    Domain = suspect.Domain,
                    ProfileId = profileId,
                    FirstSeen = suspect.FirstSeen,
                    ClassificationScore = analysisResult?.Score ?? suspect.ClassificationScore,
                    Source = "AnalysisConsumer",
                    CorrelationId = Guid.NewGuid().ToString()
                };

                await _queuePublisher.PublishAsync(queueMessage, cancellationToken);

                _logger.LogInformation(
                    "Published for cloud analysis: {Domain} (Score: {Score})",
                    suspect.Domain,
                    queueMessage.ClassificationScore);
            }

            _logger.LogInformation("AnalysisConsumer completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AnalysisConsumer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalysisConsumer failed");
            throw;
        }
    }
}
```

---

## ✏️ PASSO 4: appsettings.json

Remover configurações de import (opcionalmente manter cache):

```json
{
  "AzureStorageConnectionString": "DefaultEndpointsProtocol=https;...",
  
  "NextDns": {
    "ApiKey": "YOUR_KEY"
  },

  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "NextDnsBetBlocker.Core.Services.Queue": "Debug"
    }
  }
}
```

---

## ✅ Checklist

```
Program.cs Modifications:
☐ Remover ImportListBackgroundService registration
☐ Remover TrancoListImporter
☐ Remover ImportMetricsCollector
☐ Remover IListImportConsumer
☐ Remover IListImportOrchestrator
☐ Remover IListBlobRepository
☐ Remover ImportListBackgroundService hosted service
☐ Adicionar ISuspectDomainQueuePublisher

ClassifierConsumer:
☐ Injetar ISuspectDomainQueuePublisher
☐ Publicar queueMessage ao detectar suspeito
☐ Handle exceptions (throw ou continue?)
☐ Manter pipeline local opcional

AnalysisConsumer:
☐ Injetar ISuspectDomainQueuePublisher (OPCIONAL)
☐ Publicar para análise remota (OPCIONAL)

appsettings:
☐ Remover configurações import (OPCIONAL)
☐ Adicionar queue logging config

Build:
☐ dotnet build deve sucesso
☐ Sem erros de compilação
```

---

## 🧪 TESTAR

### 1. Build
```bash
dotnet build
```

### 2. Rodar ambos workers
```bash
# Terminal 1: Azurite
azurite --silent

# Terminal 2: Worker.Importer
cd src\NextDnsBetBlocker.Worker.Importer
dotnet run

# Terminal 3: Worker (Analysis)
cd src\NextDnsBetBlocker.Worker
dotnet run
```

### 3. Verificar Fila
```bash
# Veja mensagens em suspicious-domains queue
# Use Azure Storage Explorer
```

---

## 📊 Fluxo Final

```
Worker.Importer (LOCAL)
    ├─ ImportListBackgroundService
    └─ Runs 24/7 on your machine

          ↓ PERSISTS

Table Storage (Shared)
└─ TrancoList, BlockedDomains, etc

          ↓ LOGS

NextDNS API
    ↓

Worker (AZURE - Analysis)
    ├─ BetBlockerPipeline
    ├─ ClassifierConsumer
    │  └─ Detects suspicious
    │     ↓
    │  Publica na Queue

          ↓ EVENTS

Queue Storage
└─ suspicious-domains

          ↓ TRIGGERED

Azure Functions (OPTIONAL)
├─ AnalyzeDomainFunction
└─ BlockDomainFunction
```

---

**Status**: ✅ Pronto para modificar Worker
**Tempo estimado**: 30 minutos para modificar e testar

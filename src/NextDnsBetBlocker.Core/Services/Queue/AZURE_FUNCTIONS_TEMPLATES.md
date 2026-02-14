# 📋 EXEMPLOS DE AZURE FUNCTIONS

Estes arquivos devem ser criados em um **projeto separado** de Azure Functions.

---

## Criar Projeto Azure Functions

```bash
# Criar novo projeto
dotnet new azurefunctions -n NextDnsBetBlocker.Functions -f net8.0

cd NextDnsBetBlocker.Functions

# Add project reference para .Core
dotnet add reference ../NextDnsBetBlocker.Core/NextDnsBetBlocker.Core.csproj

# Add NuGet packages
dotnet add package Microsoft.Azure.Functions.Worker.Extensions.Storage
```

---

## 1️⃣ AnalyzeDomainFunction.cs

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Models;
using System.Text.Json;

namespace NextDnsBetBlocker.Functions
{
    public class AnalyzeDomainFunction
    {
        private readonly ILogger<AnalyzeDomainFunction> _logger;

        public AnalyzeDomainFunction(ILogger<AnalyzeDomainFunction> logger)
        {
            _logger = logger;
        }

        [Function("AnalyzeDomain")]
        [QueueOutput("domains-ready-to-block")]
        public async Task<DomainBlockRequest?> Run(
            [QueueTrigger("suspicious-domains", Connection = "AzureWebJobsStorage")] 
            string queueMessage,
            FunctionContext context)
        {
            try
            {
                var suspect = JsonSerializer.Deserialize<SuspectDomainQueueMessage>(queueMessage);

                if (suspect == null)
                {
                    _logger.LogError("Failed to deserialize: {Message}", queueMessage);
                    return null;
                }

                _logger.LogInformation(
                    "Analyzing domain: {Domain} (Score: {Score})",
                    suspect.Domain,
                    suspect.ClassificationScore);

                // TODO: Implementar análise real
                // 1. HTTP requests para VirusTotal, URLhaus, etc
                // 2. ML classification
                // 3. Database lookups
                // 4. Content analysis

                // Simular análise
                var confidenceScore = await AnalyzeDomainAsync(suspect.Domain);

                if (confidenceScore > 0.7)
                {
                    _logger.LogInformation(
                        "Domain ready for blocking: {Domain} (Confidence: {Score:P})",
                        suspect.Domain,
                        confidenceScore);

                    return new DomainBlockRequest
                    {
                        Domain = suspect.Domain,
                        ProfileId = suspect.ProfileId,
                        ConfidenceScore = confidenceScore,
                        Reason = "Detected as gambling/malicious",
                        AnalyzedAt = DateTime.UtcNow,
                        CorrelationId = suspect.CorrelationId
                    };
                }

                _logger.LogInformation(
                    "Domain confidence too low: {Domain} (Score: {Score:P})",
                    suspect.Domain,
                    confidenceScore);

                return null; // Don't block
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing domain");
                throw;
            }
        }

        private async Task<double> AnalyzeDomainAsync(string domain)
        {
            // Simulate analysis delay
            await Task.Delay(50);

            // TODO: Replace with real analysis
            // Example: Check against reputation services
            return 0.85;
        }
    }

    public class DomainBlockRequest
    {
        public string Domain { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public DateTime AnalyzedAt { get; set; }
        public double ConfidenceScore { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }
}
```

---

## 2️⃣ BlockDomainFunction.cs

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using System.Text.Json;

namespace NextDnsBetBlocker.Functions
{
    public class BlockDomainFunction
    {
        private readonly INextDnsClient _nextDnsClient;
        private readonly ILogger<BlockDomainFunction> _logger;

        public BlockDomainFunction(
            INextDnsClient nextDnsClient,
            ILogger<BlockDomainFunction> logger)
        {
            _nextDnsClient = nextDnsClient;
            _logger = logger;
        }

        [Function("BlockDomain")]
        public async Task Run(
            [QueueTrigger("domains-ready-to-block", Connection = "AzureWebJobsStorage")] 
            string queueMessage)
        {
            try
            {
                var blockRequest = JsonSerializer.Deserialize<DomainBlockRequest>(queueMessage);

                if (blockRequest == null)
                {
                    _logger.LogError("Failed to deserialize block request");
                    throw new InvalidOperationException("Invalid message format");
                }

                _logger.LogInformation(
                    "Blocking domain: {Domain} on profile {ProfileId}",
                    blockRequest.Domain,
                    blockRequest.ProfileId);

                // Call NextDNS to block
                var success = await _nextDnsClient.AddToDenylistAsync(
                    blockRequest.ProfileId,
                    new DenylistBlockRequest 
                    { 
                        Domain = blockRequest.Domain,
                        Description = blockRequest.Reason
                    });

                if (success)
                {
                    _logger.LogInformation(
                        "Domain blocked successfully: {Domain}",
                        blockRequest.Domain);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to block domain: {Domain}",
                        blockRequest.Domain);
                    throw new InvalidOperationException("NextDNS block failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error blocking domain");
                throw; // Retry
            }
        }
    }

    public class DomainBlockRequest
    {
        public string Domain { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public DateTime AnalyzedAt { get; set; }
        public double ConfidenceScore { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }
}
```

---

## 3️⃣ local.settings.json

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=...",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "NextDnsApiKey": "your-api-key-here"
  },
  "Host": {
    "LocalHttpPort": 7071
  }
}
```

---

## 4️⃣ Program.cs (Dependency Injection)

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Add HTTP client
        services.AddHttpClient<INextDnsClient, NextDnsClient>();

        // Configuration
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        
        // Add your services
        services.AddSingleton<INextDnsClient>(sp =>
            new NextDnsClient(
                sp.GetRequiredService<HttpClient>(),
                configuration["NextDnsApiKey"],
                sp.GetRequiredService<ILogger<NextDnsClient>>()));
    })
    .Build();

host.Run();
```

---

## 🧪 Testando Localmente

### 1. Instalar Azurite

```bash
npm install -g azurite
azurite --silent
```

### 2. Rodar Functions

```bash
func start
```

### 3. Publicar Mensagem

```bash
# Terminal separado
az storage message put \
  --queue-name suspicious-domains \
  --content '{"domain":"example.com","profileId":"test-profile","firstSeen":"2024-01-01T00:00:00Z","classificationScore":0.9}'
```

### 4. Verificar Resultado

```bash
# Ver fila domains-ready-to-block
az storage message peek \
  --queue-name domains-ready-to-block
```

---

## 🚀 Deploy em Azure

```bash
# Criar Storage Account (se não existe)
az storage account create \
  --name dnsblockerstorage \
  --resource-group dns-blocker \
  --location eastus \
  --sku Standard_LRS

# Criar Function App
az functionapp create \
  --resource-group dns-blocker \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --functions-version 4 \
  --name dns-blocker-functions \
  --storage-account dnsblockerstorage

# Deploy
func azure functionapp publish dns-blocker-functions
```

---

## 📊 Monitoração

```bash
# Ver logs
func azure functionapp logstream dns-blocker-functions

# Ver métricas
az monitor metrics list \
  --resource-group dns-blocker \
  --resource-type Microsoft.Web/sites \
  --resource-name dns-blocker-functions
```

---

**Pronto para criar Azure Functions!** ✅

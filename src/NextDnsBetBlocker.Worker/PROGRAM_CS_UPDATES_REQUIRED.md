## MANUAL UPDATES REQUIRED FOR Program.cs

### 1. Add Import Statement
Add this line after the existing using statements (around line 3):
```csharp
using Microsoft.Extensions.Caching.Memory;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services.Import;
```

### 2. Replace Pipeline Section (lines 100-105)
Change from:
```csharp
// Pipeline Consumers and Producer
services.AddSingleton<ILogsProducer, LogsProducer>();
services.AddSingleton<IClassifierConsumer, ClassifierConsumer>();
services.AddSingleton<ITrancoAllowlistProvider, TrancoAllowlistProvider>();
services.AddSingleton<ITrancoAllowlistConsumer, TrancoAllowlistConsumer>();
services.AddSingleton<IAnalysisConsumer, AnalysisConsumer>();
```

To:
```csharp
// Pipeline Consumers and Producer
services.AddSingleton<ILogsProducer, LogsProducer>();
services.AddSingleton<IClassifierConsumer, ClassifierConsumer>();

// Memory Cache for List Table Provider
services.AddMemoryCache();

// List Table Provider (genérico para queries no Table Storage)
services.AddSingleton<IListTableProvider>(sp =>
{
    var connString = settings.AzureStorageConnectionString;
    var tableServiceClient = new TableServiceClient(connString);
    var tableClient = tableServiceClient.GetTableClient("TrancoList");
    var cache = sp.GetRequiredService<IMemoryCache>();
    var partitionStrategy = sp.GetRequiredService<IPartitionKeyStrategy>();
    return new ListTableProvider(
        tableClient,
        cache,
        partitionStrategy,
        sp.GetRequiredService<ILogger<ListTableProvider>>());
});

services.AddSingleton<ITrancoAllowlistProvider, TrancoAllowlistProvider>();
services.AddSingleton<ITrancoAllowlistConsumer, TrancoAllowlistConsumer>();
services.AddSingleton<IAnalysisConsumer, AnalysisConsumer>();
```

### 3. Add After Gambling Suspect Analyzer (around line 126)
Add:
```csharp
// ============= IMPORT SERVICES (REFATORAÇÃO) =============

// Import Infrastructure - Metrics, Rate Limiting, Partitioning
services.AddSingleton<IImportMetricsCollector, ImportMetricsCollector>();
services.AddSingleton<IPartitionKeyStrategy>(sp => new PartitionKeyStrategy(10));
services.AddSingleton<IImportRateLimiter>(sp => new ImportRateLimiter(150000));

// Import Producer & Consumer
services.AddHttpClient<IListImportProducer, ListImportProducer>();
services.AddSingleton<IListImportConsumer, ListImportConsumer>();
services.AddSingleton<IListImportOrchestrator, ListImportOrchestrator>();

// Import Repositories - Table Storage & Blob Storage
services.AddSingleton<IListTableStorageRepository>(sp =>
{
    var connString = settings.AzureStorageConnectionString;
    return new ListTableStorageRepository(
        connString,
        "TrancoList",
        sp.GetRequiredService<ILogger<ListTableStorageRepository>>());
});

services.AddSingleton<IListBlobRepository>(sp =>
{
    var connString = settings.AzureStorageConnectionString;
    return new ListBlobRepository(
        connString,
        "tranco-lists",
        sp.GetRequiredService<ILogger<ListBlobRepository>>());
});

// Import Generic Importer
services.AddSingleton<IListImporter>(sp =>
{
    return new GenericListImporter(
        sp.GetRequiredService<ILogger<GenericListImporter>>(),
        sp.GetRequiredService<IListImportOrchestrator>(),
        sp.GetRequiredService<IListBlobRepository>(),
        sp.GetRequiredService<IListTableStorageRepository>());
});

// Import Tranco Specific
services.AddSingleton<TrancoListImporter>();

// Import Configuration
var trancoConfig = TrancoListImporter.CreateConfig();
services.AddSingleton(trancoConfig);

// Import Background Service
services.AddHostedService<ImportListBackgroundService>();

// ============= END IMPORT SERVICES =============
```

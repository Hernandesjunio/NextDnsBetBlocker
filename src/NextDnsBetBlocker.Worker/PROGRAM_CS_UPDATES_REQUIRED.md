## MANUAL UPDATES REQUIRED FOR Program.cs

### 1. Add Import Statements
Add these lines after the existing using statements (around line 3):
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

---

## 4. Add Table Initialization (CRITICAL)

After `.Build()` (around line 145), add this code BEFORE running the host:

```csharp
var host = new HostBuilder()
    // ... all configuration ...
    .Build();

// ============= INITIALIZE LIST TABLES =============
try
{
    var tableInitializer = host.Services.GetRequiredService<ListTableInitializer>();
    await tableInitializer.InitializeAllTablesAsync();

    _logger.LogInformation("All list tables initialized successfully");
}
catch (Exception ex)
{
    var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("ListTableInitialization");
    logger.LogError(ex, "Failed to initialize list tables");
    throw; // Fail startup if table initialization fails
}
// ============= END TABLE INITIALIZATION =============

// Seed checkpoint data before running
if (_checkpointTableClient != null)
{
    await SeedCheckpointAsync(_checkpointTableClient);
}

// ... rest of initialization ...

await host.RunAsync();
```

**IMPORTANTE**: Este passo **MUST** acontecer APÓS `.Build()` e ANTES de `await host.RunAsync()`

---

## 5. Register ListTableInitializer in DI (Inside ConfigureServices)

Add this BEFORE the settings are used (around line 125):

```csharp
// List Table Initializer
services.AddSingleton<ListTableInitializer>();
```

---

## 📝 Summary of Changes

1. ✅ Add 3 using statements
2. ✅ Update Pipeline section (add IListTableProvider + TrancoAllowlistProvider)
3. ✅ Add 40 lines of Import Services DI registration
4. ✅ Add ListTableInitializer DI registration
5. ✅ Add table initialization in startup (critical)

**Total lines added**: ~120 lines

---

## ⚠️ CRITICAL NOTES

- **Table initialization MUST be after `.Build()`** to access `host.Services`
- **If table creation fails, startup should fail** (see `throw` in catch)
- **Logging will show**: "TrancoList table initialized successfully"
- **First run will take 5-10 seconds longer** (creating tables)
- **Subsequent runs will be instant** (tables already exist)
## ✅ CENTRALIZAÇÃO COMPLETA: StorageInfrastructureInitializer

### O Que Foi Criado

1. **IStorageInfrastructureInitializer.cs** - Interface genérica
2. **StorageInfrastructureInitializer.cs** - Implementação centralizada

---

## 🎯 Mudanças Necessárias em Program.cs

### ANTES (Lógica distribuída)
```csharp
// Azure Storage (linhas 46-67)
var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
var tableClient = tableServiceClient.GetTableClient("BlockedDomains");
var checkpointTableClient = tableServiceClient.GetTableClient("AgentState");
var suspectTableClient = tableServiceClient.GetTableClient("GamblingSuspects");

tableClient.CreateIfNotExists();
checkpointTableClient.CreateIfNotExists();
suspectTableClient.CreateIfNotExists();

// ... então depois, duplicado em outras partes
```

### DEPOIS (Centralizado)
```csharp
// Dentro de ConfigureServices:

// Register Storage Infrastructure Initializer
services.AddSingleton<IStorageInfrastructureInitializer>(sp =>
{
    var tableRepo = sp.GetRequiredService<IListTableStorageRepository>();
    var blobClient = settings.UseBlobStorage && !string.IsNullOrEmpty(settings.AzureStorageConnectionString)
        ? new BlobServiceClient(settings.AzureStorageConnectionString).GetBlobContainerClient("default")
        : null;
    
    return new StorageInfrastructureInitializer(
        tableRepo,
        blobClient,
        sp.GetRequiredService<ILogger<StorageInfrastructureInitializer>>());
});

// Depois em Main (após .Build()):
var storageInit = host.Services.GetRequiredService<IStorageInfrastructureInitializer>();
await storageInit.InitializeAsync();
```

---

## 📋 Passos de Implementação

### 1. Add Using Statements
```csharp
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Services.Storage;
```

### 2. REMOVER Lógica Duplicada (linhas 46-67 aprox.)
```csharp
// REMOVER ISSO:
if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
{
    var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
    var tableClient = tableServiceClient.GetTableClient("BlockedDomains");
    var checkpointTableClient = tableServiceClient.GetTableClient("AgentState");
    var suspectTableClient = tableServiceClient.GetTableClient("GamblingSuspects");

    tableClient.CreateIfNotExists();
    checkpointTableClient.CreateIfNotExists();
    suspectTableClient.CreateIfNotExists();
    
    // ... resto do código
}
```

### 3. MANTER APENAS (em ConfigureServices)
```csharp
// Azure Storage
if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
{
    var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
    var tableClient = tableServiceClient.GetTableClient("BlockedDomains");
    var checkpointTableClient = tableServiceClient.GetTableClient("AgentState");
    var suspectTableClient = tableServiceClient.GetTableClient("GamblingSuspects");

    // NÃO CRIAR AQUI - vai ser feito pelo inicializador
    // tableClient.CreateIfNotExists();
    // checkpointTableClient.CreateIfNotExists();
    // suspectTableClient.CreateIfNotExists();

    _checkpointTableClient = checkpointTableClient;

    services.AddSingleton(tableClient);
    services.AddSingleton(suspectTableClient);
    services.AddSingleton<IBlockedDomainStore>(sp => new BlockedDomainStore(tableClient, sp.GetRequiredService<ILogger<BlockedDomainStore>>()));
    services.AddSingleton<ICheckpointStore>(sp => new CheckpointStore(checkpointTableClient, sp.GetRequiredService<ILogger<CheckpointStore>>()));
    services.AddSingleton<IGamblingSuspectStore>(sp => new GamblingSuspectStore(suspectTableClient, sp.GetRequiredService<ILogger<GamblingSuspectStore>>()));
}
```

### 4. ADD DI Registration (em ConfigureServices, no final)
```csharp
// ============= STORAGE INFRASTRUCTURE INITIALIZATION =============
services.AddSingleton<IStorageInfrastructureInitializer>(sp =>
{
    var tableRepo = sp.GetRequiredService<IListTableStorageRepository>();
    var blobClient = settings.UseBlobStorage && !string.IsNullOrEmpty(settings.AzureStorageConnectionString)
        ? new BlobServiceClient(settings.AzureStorageConnectionString).GetBlobContainerClient("default")
        : null;
    
    return new StorageInfrastructureInitializer(
        tableRepo,
        blobClient,
        sp.GetRequiredService<ILogger<StorageInfrastructureInitializer>>());
});
// ============= END STORAGE INFRASTRUCTURE =============
```

### 5. ADD Initialization Call (após .Build(), ANTES de host.RunAsync())
```csharp
var host = new HostBuilder()
    // ... config ...
    .Build();

// ============= INITIALIZE STORAGE INFRASTRUCTURE =============
try
{
    _logger.LogInformation("Initializing storage infrastructure...");
    var storageInit = host.Services.GetRequiredService<IStorageInfrastructureInitializer>();
    await storageInit.InitializeAsync();
}
catch (Exception ex)
{
    var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("StorageInitialization");
    logger.LogError(ex, "Failed to initialize storage infrastructure");
    throw;
}
// ============= END STORAGE INITIALIZATION =============

// Existing initialization code continues...
if (_checkpointTableClient != null)
{
    await SeedCheckpointAsync(_checkpointTableClient);
}

// ... rest of code
```

---

## 🎯 Resultado

### Antes
```
Program.cs
├─ Lines 46-67: CreateIfNotExists (duplicado)
├─ ConfigureServices: mais 50+ linhas
└─ Main: Sem centralização
```

### Depois
```
StorageInfrastructureInitializer.cs
├─ Todas as tabelas gerenciadas
├─ Todos os containers gerenciados
├─ Logging centralizado
└─ Idempotente e fail fast

Program.cs
├─ ConfigureServices: +1 DI registration
├─ Main: +1 await call
└─ Clean and simple
```

---

## ✅ Tabelas/Containers Gerenciados

### Tabelas
```
✓ AgentState          (Checkpoint tracking)
✓ BlockedDomains      (Domínios bloqueados)
✓ GamblingSuspects    (Suspeitos de jogo)
✓ TrancoList          (Lista Tranco confiável)
```

### Containers
```
✓ hagezi-gambling     (HaGeZi list)
✓ tranco-lists        (Tranco files)
```

---

## 📊 Benefícios

✅ **Centralizado**: Uma única classe gerencia tudo
✅ **Idempotente**: Safe to call multiple times
✅ **Logging**: Visibilidade completa
✅ **Fail fast**: Erro aborta startup
✅ **Extensível**: Adicionar tabelas/containers é trivial
✅ **Testável**: Pode ser testado isoladamente

---

## 🚀 Próximos Passos

1. Add usings em Program.cs
2. Remover lógica de CreateIfNotExists (linhas 51-53)
3. Add DI registration para StorageInfrastructureInitializer
4. Add initialization call em Main
5. Compilar e validar

Tempo estimado: ~15 minutos

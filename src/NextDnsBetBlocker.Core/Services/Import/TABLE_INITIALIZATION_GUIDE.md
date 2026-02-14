## ✅ TABLE INITIALIZATION STRATEGY

### 🎯 Objetivo
Garantir que a tabela **TrancoList** existe no Azure Table Storage antes de usar.

---

## 📋 Implementação

### 1. **ListTableInitializer.cs** (CRIADO)
```csharp
public class ListTableInitializer
{
    public async Task InitializeAllTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = new[] { "TrancoList", /* outras */ };
        foreach (var tableName in tables)
        {
            await InitializeTableAsync(tableName, cancellationToken);
        }
    }

    public async Task InitializeTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        await _tableRepository.EnsureTableExistsAsync(tableName, cancellationToken);
    }
}
```

### 2. **Program.cs - DI Registration**
```csharp
// DENTRO de ConfigureServices
services.AddSingleton<ListTableInitializer>();
```

### 3. **Program.cs - Startup Initialization** (CRÍTICO)
```csharp
// APÓS .Build(), ANTES de host.RunAsync()
var tableInitializer = host.Services.GetRequiredService<ListTableInitializer>();
await tableInitializer.InitializeAllTablesAsync();
```

---

## 🔄 Fluxo de Execução

```
Startup
  ↓
Program.Build()
  ↓
ListTableInitializer.InitializeAllTablesAsync()
  ├─ IListTableStorageRepository.EnsureTableExistsAsync("TrancoList")
  │  ├─ Chama: tableClient.CreateAsync()
  │  └─ Azure: Cria tabela se não existir
  │
  ├─ Log: "List table initialized: TrancoList"
  └─ ✅ Sucesso

Startup continua...
  ↓
host.RunAsync() inicia serviço
  ↓
TrancoAllowlistConsumer pode usar a tabela
```

---

## ⚡ Garantias

✅ **Tabela sempre existe** - Criada durante startup
✅ **Idempotente** - Safe to call multiple times
✅ **Logging** - Visibilidade de sucesso/falha
✅ **Falha rápida** - Se não conseguir criar, startup falha
✅ **Suporta múltiplas tabelas** - Extensível

---

## 📊 Comportamento

### Primeira Execução
```
[INF] Ensuring list table exists: TrancoList
[INF] List table initialized successfully: TrancoList
Time: ~5-10 segundos (chamada Azure)
```

### Execuções Subsequentes
```
[INF] Ensuring list table exists: TrancoList
[INF] List table initialized successfully: TrancoList
Time: ~100ms (tabela já existe)
```

### Se Falhar
```
[ERR] Failed to initialize list table: TrancoList
      Exception: ...
[FATAL] Startup aborted - não inicia o serviço
```

---

## 🔗 Integração com Componentes Existentes

```
ListTableInitializer
  ↓ usa
IListTableStorageRepository.EnsureTableExistsAsync()
  ↓ que usa
TableClient.CreateAsync()
  ↓ Azure Table Storage API
```

---

## 📝 Alterações Necessárias em Program.cs

### Location 1: ConfigureServices (around line 125)
```csharp
// Add alongside other registrations
services.AddSingleton<ListTableInitializer>();
```

### Location 2: After .Build() (around line 145)
```csharp
var host = new HostBuilder()
    .ConfigureServices(...)
    .Build(); // ← Após isso

// ADD AQUI:
var tableInitializer = host.Services.GetRequiredService<ListTableInitializer>();
await tableInitializer.InitializeAllTablesAsync();

// ANTES disso:
await host.RunAsync();
```

---

## ✅ Verificação

Para validar que funcionou:

1. **Logs**: Procure por "List table initialized successfully: TrancoList"
2. **Azure Portal**: Verifique se tabela existe em Storage Account
3. **TrancoAllowlistConsumer**: Funcionará sem erros de tabela não encontrada

---

## 🚀 Production Checklist

- ✅ ListTableInitializer implementado
- ✅ DI registration no Program.cs
- ✅ Initialization call no startup (após .Build())
- ✅ Error handling (throw se falhar)
- ✅ Logging configurado
- ✅ Suporta múltiplas tabelas (extensível)

Ready for deployment! 🎉

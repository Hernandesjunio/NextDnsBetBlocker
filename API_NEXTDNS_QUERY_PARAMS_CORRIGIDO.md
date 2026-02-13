# ✅ CORREÇÃO API NextDNS - Query Parameters

## 🔧 Problema Identificado

A API do NextDNS **não usa** `since`, mas sim **`from` e `to`** para filtrar logs por timestamp.

### Query Parameters da API:
```
GET /profiles/:profile/logs
  ?limit=1000
  &sort=asc
  &from={unix_timestamp}    ← Timestamps em Unix (segundos)
  &to={unix_timestamp}
  &cursor={pagination}
```

---

## ✅ Correções Implementadas

### 1️⃣ **NextDnsClient.cs - Usar `from` em vez de `since`**

```csharp
// ANTES (ERRADO)
if (since.HasValue)
{
    var unixTimestamp = ((DateTimeOffset)since.Value.ToUniversalTime()).ToUnixTimeSeconds();
    url += $"&since={unixTimestamp}";  // ❌ Parâmetro inválido
}

// DEPOIS (CORRETO)
if (from.HasValue)
{
    var fromTimestamp = ((DateTimeOffset)from.Value.ToUniversalTime()).ToUnixTimeSeconds();
    url += $"&from={fromTimestamp}";   // ✅ Parâmetro correto
}
```

### 2️⃣ **Novo Método: `GetLogsRangeAsync`**

Para suporte completo a range de datas:

```csharp
public async Task<NextDnsLogsResponse> GetLogsRangeAsync(
    string profileId,
    string? cursor = null,
    int limit = 1000,
    DateTime? from = null,
    DateTime? to = null)
```

**Uso:**
```csharp
// Apenas logs a partir de um timestamp
var logs = await client.GetLogsAsync(profileId, since: lastCheckpoint);

// OU com range completo (from/to)
var logs = await client.GetLogsRangeAsync(
    profileId, 
    from: DateTime.UtcNow.AddDays(-1),
    to: DateTime.UtcNow);
```

### 3️⃣ **Interface INextDnsClient Atualizada**

```csharp
public interface INextDnsClient
{
    // Compatível com checkpoint (usa 'from')
    Task<NextDnsLogsResponse> GetLogsAsync(
        string profileId, 
        string? cursor = null, 
        int limit = 1000, 
        DateTime? since = null);

    // Novo método com suporte a 'from' e 'to'
    Task<NextDnsLogsResponse> GetLogsRangeAsync(
        string profileId, 
        string? cursor = null, 
        int limit = 1000, 
        DateTime? from = null, 
        DateTime? to = null);

    Task<bool> AddToDenylistAsync(string profileId, DenylistBlockRequest request);
}
```

---

## 📊 Exemplos de URL Gerada

### Antes (ERRADO):
```
https://api.nextdns.io/profiles/71cb47/logs
  ?limit=1000
  &sort=asc
  &since=1705333970  ❌ Parâmetro inválido
  &cursor=abc123
```

### Depois (CORRETO):
```
https://api.nextdns.io/profiles/71cb47/logs
  ?limit=1000
  &sort=asc
  &from=1705333970   ✅ Parâmetro correto
  &cursor=abc123
```

### Com Range (NOVO):
```
https://api.nextdns.io/profiles/71cb47/logs
  ?limit=1000
  &sort=asc
  &from=1705247570   ← 1 dia atrás
  &to=1705333970     ← Agora
  &cursor=abc123
```

---

## 🧪 Conversão de Timestamp

```csharp
// DateTime → Unix Timestamp
var utcDateTime = DateTime.UtcNow;
var unixTimestamp = ((DateTimeOffset)utcDateTime).ToUnixTimeSeconds();

// Exemplo:
// 2024-01-15T14:32:50Z → 1705333970

// Unix Timestamp → DateTime
var dateTime = DateTimeOffset.FromUnixTimeSeconds(1705333970).UtcDateTime;
// → 2024-01-15T14:32:50Z
```

---

## ✨ Benefícios

✅ **Checkpoint funciona corretamente** - Filtra logs desde a última execução  
✅ **Menos dados transferidos** - API filtra no servidor  
✅ **Range support** - Pode buscar período específico  
✅ **Backwards compatible** - Mantém `GetLogsAsync` para checkpoint  
✅ **Build**: ✅ Sucesso  

---

## 🎯 Próximo Passo

Agora o checkpoint deve funcionar corretamente pois a API vai respeitar o filtro `from` e retornar apenas logs novos!

Exemplo de log esperado:
```
info: NextDnsBetBlocker.Core.Services.NextDnsClient
      Filtering logs from: 2024-01-15T14:32:50.0000000Z (Unix: 1705333970)
      Requesting logs from URL: https://api.nextdns.io/profiles/71cb47/logs?limit=1000&sort=asc&from=1705333970
      Received 0 logs from NextDNS  ← Sem duplicação!
```

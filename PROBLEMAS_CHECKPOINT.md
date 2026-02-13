# 🔴 PROBLEMAS DE CHECKPOINT IDENTIFICADOS

## ❌ Problemas Críticos Encontrados:

### 1. **NextDnsClient NÃO filtra por data**
```csharp
// ERRADO - Busca TODOS os logs, sem filtro de data
var url = $"{BaseUrl}/profiles/{profileId}/logs?limit={limit}&sort=asc";
```

**Solução**: Adicionar parâmetro `since` para buscar apenas logs após o checkpoint

### 2. **CheckpointStore - Conversão de DateTime com Timezone**
```csharp
// POSSÍVEL PROBLEMA - DateTime pode estar em UTC vs Local
if (entity.TryGetValue("LastTimestamp", out var lastTimestamp) && lastTimestamp is DateTime dt)
```

**Solução**: Garantir que todos os timestamps sejam `DateTime.UtcNow`

### 3. **Comparação com >= ao invés de >**
```csharp
// Pode pular o log exatamente no timestamp do checkpoint
if (log.Timestamp > (lastTimestamp ?? DateTime.MinValue))
```

**Solução**: Usar `>=` para capturar o exato log do checkpoint

### 4. **Microsegundos em Timestamp**
NextDNS API pode retornar timestamps com microsegundos diferentes, causando comparação falha

**Solução**: Arredondar timestamps para segundo mais próximo

### 5. **Checkpoint pode não estar sendo salvo**
Verificar se `UpdateLastTimestampAsync` está sendo chamado no final do pipeline

---

## 📋 Plano de Correção:

1. ✅ Adicionar `since` ao `GetLogsAsync`
2. ✅ Padronizar todos timestamps como UTC
3. ✅ Usar `>=` nas comparações
4. ✅ Arredondar timestamps para evitar microsegundos
5. ✅ Adicionar logging detalhado
6. ✅ Validar checkpoint está sendo salvo

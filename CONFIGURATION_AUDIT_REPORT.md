# 🔍 AUDIT - CLASSES COM CONFIGURAÇÃO HARDCODED

## ⚠️ CRÍTICAS ENCONTRADAS

### 🚨 **ALTO RISCO: NextDnsClient.cs**

```csharp
private const string BaseUrl = "https://api.nextdns.io";
private const string ApiKey = "9a0a16ee3b58ef4e894fe75b51caa354d9dec3e2";  // ← HARDCODED!
```

**Problemas:**
- ❌ API Key em código-fonte (SECURITY BREACH!)
- ❌ Não lê de appsettings.json
- ❌ Não usa `IOptions<T>`
- ❌ Constructor não injeta config

**Severidade**: 🚨 **CRÍTICA**

---

### ⚠️ **ALTO RISCO: HageziProvider.cs**

```csharp
private const string HageziUrlAdblock = "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/adblock/gambling.txt";
private const string HageziUrlWildcard = "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/gambling.txt";
```

**Problemas:**
- ❌ URLs hardcoded
- ❌ Não lê de appsettings.json
- ❌ Não usa `IOptions<T>`
- ❌ Difícil mudar em diferentes ambientes

**Severidade**: ⚠️ **ALTA**

---

## 📊 RESUMO DO AUDIT

| Classe | Status | Severidade | Ação |
|--------|--------|-----------|------|
| **NextDnsClient** | ❌ Hardcoded | 🚨 CRÍTICA | Refatorar URGENTE |
| **HageziProvider** | ❌ Hardcoded | ⚠️ ALTA | Refatorar |
| **TrancoListImporter** | ✅ IOptions | ✅ OK | - |
| **ImportListBackgroundService** | ✅ IOptions | ✅ OK | - |
| **TrancoAllowlistProvider** | ✅ IOptions | ✅ OK | - |
| **BetBlockerPipeline** | ✅ OK | ✅ OK | - |

---

## 🎯 PLANO DE AÇÃO

### **Prioridade 1: NextDnsClient (SECURITY)**
```
1. Criar IOptions<NextDnsClientConfig>
2. Mover ApiKey para appsettings.json (seguro com User Secrets)
3. Injetar em NextDnsClient
4. Atualizar CoreServiceCollectionExtensions
```

### **Prioridade 2: HageziProvider**
```
1. Criar IOptions<HageziProviderConfig>
2. Mover URLs para appsettings.json
3. Injetar em HageziProvider
4. Atualizar CoreServiceCollectionExtensions
```

---

## 💡 ESTRUTURA ESPERADA (appsettings.json)

```json
{
  "NextDns": {
    "ApiKey": "9a0a16ee3b58ef4e894fe75b51caa354d9dec3e2",
    "BaseUrl": "https://api.nextdns.io",
    "RateLimitPerSecond": 1000
  },
  "HaGeZi": {
    "AdblockUrl": "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/adblock/gambling.txt",
    "WildcardUrl": "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/gambling.txt",
    "CacheExpireHours": 1
  }
}
```

---

## ✅ PRÓXIMOS PASSOS

Quer que eu refatore:
1. ✅ **NextDnsClient** (URGENTE - SECURITY)
2. ✅ **HageziProvider** (depois)

**Recomendação**: Fazer NextDnsClient primeiro (segurança crítica)!

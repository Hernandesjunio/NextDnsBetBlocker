# NextDnsBetBlocker Importer 📥

## Visão Geral

O **Importer** é um serviço console .NET que executa **uma única vez por dia** em Azure Container Instances (ACI). Sua responsabilidade é:

1. **Buscar listas de domínios** de fonte pública (Hagezi)
2. **Validar e deduplicar** os domínios
3. **Particionar e batch** para otimizar I/O no Table Storage
4. **Armazenar com histórico** em Table Storage para acesso posterior
5. **Prover checkpoints** para recuperação de falhas

---

## Responsabilidades

```
┌─────────────────────────────────────────────────────┐
│        NextDnsBetBlocker Importer Pipeline          │
├─────────────────────────────────────────────────────┤
│                                                      │
│  1️⃣  Fetch Lists (Hagezi)                           │
│      ├─ Download HTTP                               │
│      └─ Validação de formato                        │
│                                                      │
│  2️⃣  Parse & Validate                               │
│      ├─ Parsing de domínios                         │
│      ├─ Deduplicate in-memory                       │
│      └─ Log invalids                                │
│                                                      │
│  3️⃣  Partition by Hash                              │
│      ├─ Estratégia: hash(domain) % N partições     │
│      └─ Distribuição uniforme                       │
│                                                      │
│  4️⃣  Parallel Batch Processing                      │
│      ├─ Adaptive parallelism (5-30 tasks)          │
│      ├─ Batch size: 100 items                       │
│      ├─ Rate limiting: 2k ops/s per partition      │
│      └─ Exponential backoff on timeout             │
│                                                      │
│  5️⃣  Insert to Table Storage                        │
│      ├─ PartitionKey: list_name + hash_partition   │
│      └─ RowKey: domain                              │
│                                                      │
│  6️⃣  Update Checkpoint                              │
│      ├─ Mark list as imported                       │
│      └─ Record timestamp                            │
│                                                      │
└─────────────────────────────────────────────────────┘
```

---

## Arquitetura da Pipeline

### Fluxo Sequencial: Hagezi → Validação → Armazenamento

```
Input (Hagezi Lists)
      │
      ▼
┌──────────────────────────┐
│  HttpDownloadService     │  ◄── Throttling externo
│  - Download lists        │      (respeita 429 Rate Limits)
│  - Timeout: 30s          │
└──────────────────────────┘
      │
      ▼
┌──────────────────────────┐
│  GenericListImporter     │
│  - Parse domains         │
│  - Validate format       │
│  - Deduplicate           │
│  - Log metrics           │
└──────────────────────────┘
      │
      ▼
┌──────────────────────────┐
│ PartitionKeyStrategy     │  ◄── Estratégia: MD5(domain) % N_partitions
│ - Hash partitioning      │      (garante distribuição uniforme)
└──────────────────────────┘
      │
      ▼
┌──────────────────────────────────────┐
│ ParallelBatchManager                 │  ◄── Core da otimização
│ ┌────────────────────────────────────┤
│ │ Partição 00                        │
│ │  Channel (batch queue)             │
│ │  ├─ RateLimiter (2k ops/s)        │
│ │  ├─ SemaphoreSlim (concurrency)   │
│ │  └─ Exponential backoff           │
│ │                                    │
│ │ Consumer Task (batch processing)   │
│ └────────────────────────────────────┤
│ ├────────────────────────────────────┤
│ │ Partição 01, 02, ..., N            │
│ └────────────────────────────────────┘
│                      │
│  Global RateLimiter (20k ops/s)
│  Global SemaphoreSlim (30 HTTP tasks)
│
└──────────────────────────────────────┘
      │
      ▼
┌──────────────────────────┐
│ ListTableStorageRepository│
│ - Batch insert           │
│ - Partition strategy     │
│ - Error handling         │
└──────────────────────────┘
      │
      ▼
┌──────────────────────────┐
│ Azure Table Storage      │
│ - Hagezi_Blocklists      │
│ - Tranco_TopDomains      │
└──────────────────────────┘
      │
      ▼
┌──────────────────────────┐
│ CheckpointStore          │
│ - Mark list as imported  │
│ - Record timestamp       │
│ - Enable recovery        │
└──────────────────────────┘
```

---

## Design Patterns & Técnicas

### 1. **Adaptive Parallelism** 🎯

**Problema**: Não sabemos quantas tarefas paralelas a rede/API suporta  
**Solução**: Começar com um grau inicial e reduzir 5% a cada timeout

```
Monitoramento em tempo real:
- RecordTimeout() → Reduz grau em 5%
- RecordSuccess() → Mantém atual
- GetStats() → Retorna (timeouts, successes, current, initial)

Exemplo:
Initial = 30
├─ Sucesso x10 → mantém 30
├─ Timeout    → reduz para 29 (30 * 0.95)
├─ Timeout    → reduz para 27 (29 * 0.95)
└─ Mín = 5 tasks (floor garantido)
```

**Benefício**: Recuperação automática sob sobrecarga sem configuração manual

### 2. **Hierarchical Throttling** 🚦

Múltiplas camadas de rate limiting para evitar saturação:

```
Per-Partition Rate Limiter (2k ops/s)
        ↓
    Per-Partition SemaphoreSlim (concurrency limit)
        ↓
    Global Rate Limiter (20k ops/s)
        ↓
    Global SemaphoreSlim (30 HTTP tasks máximo)
        ↓
    External API (Hagezi/HTTP)
```

**Implementação**:
- `ImportRateLimiter`: Sliding window (1s) com Queue<timestamp>
- `SemaphoreSlim`: Limite físico de concorrência
- Backpressure: Channels bounded causam WaitAsync se cheios
- **⚙️ [RECENTE FIX]**: Burst rate agora sincronizado corretamente com effective rate. Ver [THROTTLING_IMPROVEMENTS.md](THROTTLING_IMPROVEMENTS.md) para detalhes

### 3. **Graceful Degradation - Por Partição** ⚡

Falha isolada de uma partição não afeta outras:

```
Partição_00: Timeout
  ├─ aplica exponential backoff (100ms → 200ms → 400ms...)
  ├─ tenta retry (máx 3x)
  └─ se falhar: log + continue (não bloqueia)

Partição_01, 02, ..., N:
  └─ continuam processando normalmente
```

**Resultado**: Throughput degradado mas não zero

### 4. **Distributed Batch Isolation** 📦

Cada partição tem seu próprio:
- **Channel**: Producer/Consumer desacoplado
- **RateLimiter**: Controle independente de throughput
- **SemaphoreSlim**: Limite de concorrência isolado
- **Backoff**: Retry strategy própria

```
ParallelBatchManager
├─ PartitionConsumer (partition_00)
│  ├─ Channel (100 items max)
│  ├─ RateLimiter (2k ops/s)
│  ├─ SemaphoreSlim (concurrency)
│  └─ CurrentBackoff (retry strategy)
├─ PartitionConsumer (partition_01)
│  └─ [idem]
└─ Global: RateLimiter (20k) + SemaphoreSlim (30)
```

**Exemplo**:
- partition_00 sofre timeout → backoff = 400ms
- partition_01 continua com backoff = 0 (normal)

### 5. **Producer-Consumer com Pipelining** 🔄

Desacoplamento entre produção de batches e consumo (processamento HTTP):

```
Main Thread (Producer)
  ├─ Parse domains (CPU-bound)
  ├─ Form batches (100 items)
  └─ Enqueue to Channel (async, non-blocking)
       │
       ▼ Channel (bounded)
       │
  Consumer Tasks (per partition)
  ├─ Wait for rate limit
  ├─ Acquire semaphore slot
  ├─ HTTP POST to Table Storage (I/O)
  ├─ Release semaphore
  └─ Repeat
```

**Benefício**: CPU e I/O não se bloqueiam; máxima eficiência

---

## Configuração

### appsettings.json

```json
{
  "ListImportConfig": [
    {
      "ListName": "Hagezi_Blocklists",
      "SourceUrl": "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/fake-phishing-awareness.txt",
      "Description": "Hagezi Fake Phishing Awareness List"
    },
    {
      "ListName": "Tranco_TopDomains",
      "SourceUrl": "https://tranco-list.eu/top-4.8m.csv.zip",
      "Description": "Tranco Top 4.8M Legitimate Domains"
    }
  ],
  "ParallelImportConfig": {
    "InitialDegreeOfParallelism": 20,
    "BatchSize": 100,
    "ChannelCapacityPerPartition": 100,
    "MaxConcurrencyPerPartition": 5,
    "MaxOperationsPerSecondPerPartition": 2000,
    "MaxGlobalOperationsPerSecond": 20000,
    "MaxGlobalConcurrentRequests": 30,
    "PartitionCount": 32,
    "MaxRetries": 3
  }
}
```

### Tuning para Different Environments

| Ambiente | InitialParallelism | PartitionCount | BatchSize | MaxOpsPerSecond | Rationale |
|----------|------------------|-----------------|-----------|-----------------|-----------|
| Local Dev | 5 | 8 | 100 | 2k (global) | Evita saturação de conexões |
| Staging | 15 | 16 | 100 | 10k (global) | Teste antes de produção |
| Production | 25-30 | 32 | 100 | 20k (global) | Máxima throughput com safety |
| **Optimized Prod** | 25-30 | 32 | **500** | 20k (global) | **80% cost reduction** (future) |

**Nota**: Recommended upgrade path:
1. Deploy with BatchSize=100 (safe, proven)
2. Monitor for 1-2 weeks
3. Upgrade to BatchSize=500 if no 429 errors
4. Monitor for 1-2 weeks
5. Tune parallelism based on observed throughput

**Scaling Guidelines for Larger Datasets**:
```
< 1M items:     InitialParallelism=10, PartitionCount=8
1M - 5M items:  InitialParallelism=20, PartitionCount=16-32  ← CURRENT
5M - 50M items: InitialParallelism=30, PartitionCount=64
> 50M items:    Consider: Multi-table strategy or Premium tier
```

---

## Performance Characteristics

### Throughput Esperado

```
Cenário: 5M domínios (Tranco 4.8M + Hagezi Blocklists)

Com Adaptive Parallelism:
├─ Batch size: 100
├─ Rate limit: 2k ops/s per partition
├─ Partitions: 32
├─ Effective throughput: 32 * 2k = ~64k ops/s (teórico)
├─ Real (com latência HTTP 200ms): ~10k-15k ops/s
└─ Time to import: ~350-500s (~6-8 minutos)

vs. Sequential (1 thread):
├─ Rate limit: 2k ops/s
├─ HTTP latency: 200ms (5 parallel batches máximo)
└─ Time to import: ~2500-3000s (40-50 minutos, 5-7x mais lento)

Note: Tranco 4.8M é 4.8x maior que 1M, mas throughput escalável mantém tempo linear
```

### Memory Footprint

```
Channel buffer: 32 partitions × 100 items = 3.2k items
Per item: ~100 bytes (domain string + metadata)
Total: ~320 KB (negligível)

Deduplication (in-memory): ~240MB (para 4.8M domínios com HashSet<string>)
Overall: <400 MB para operação completa com Tranco full + Hagezi lists
```

### CPU Utilization

```
Producer (parsing): 1 core (~30% utilization)
Consumers (I/O wait): 8 cores (~5% utilization)
- Maioria do tempo esperando HTTP response (I/O bound)
- Minimal CPU contention
```

---

## Azure Table Storage Optimization & Quota Management 🎯

**📖 Para guia operacional detalhado, veja [TABLE_STORAGE_OPERATIONAL_GUIDE.md](TABLE_STORAGE_OPERATIONAL_GUIDE.md)**

Nesta seção documentamos estratégias arquiteturais. Para troubleshooting em tempo real e checklists operacionais, consulte o guia acima.

### Contexto: Operando Próximo aos Limites Máximos

Com 5M+ domínios importados, o Table Storage requer **estratégia cuidadosa** para evitar throttling (429) e maximizar throughput:

```
Azure Table Storage Limits (por partition key):
├─ Throughput: 20,000 RUs/s (eventual consistency)
├─ Entity size: 1 MB máximo
├─ Batch size: 100 entidades máximo
├─ Request rate: ~20k requests/s por partition
└─ Concurrent connections: Limited by storage account
```

### Estratégia de Particionamento para Escalabilidade

A escolha do **PartitionKey** é crítica para evitar hot-spots:

```
Nossa estratégia: hash(domain) % N_partitions

Exemplo: 32 partições
├─ example.com → MD5 hash → 0x3f... → 0x3f % 32 = partition_15
├─ google.com  → MD5 hash → 0x7a... → 0x7a % 32 = partition_26
└─ amazon.com  → MD5 hash → 0x1b... → 0x1b % 32 = partition_27

Distribuição uniforme:
├─ Sem hot-spots (cada partição recebe ~156k domínios = 5M/32)
├─ Throughput balanceado entre partições
└─ Escalável: adicione partições se N aumentar
```

### Cálculo de Throughput Consumido

```
Throughput Storage Unit (TU) = (Data written / 1 KB) + (Operations / 100)

Cenário: 5M domínios, 1x/dia

Dados por entidade:
├─ PartitionKey: ~20 bytes (e.g., "Tranco_4")
├─ RowKey: ~50 bytes (domain name)
├─ Timestamp: 8 bytes (automático)
├─ properties: category, source, timestamp_imported (~100 bytes)
└─ Total por entidade: ~178 bytes

Cálculo diário:
├─ Write operations: 5,000,000 inserts
├─ Data written: 5M * 178 bytes = ~890 MB = ~890,000 KB
├─ TU = (890,000 / 1) + (5,000,000 / 100) = 890,000 + 50,000 = 940,000 TUs/dia
├─ Spread over 7 minutos: 940,000 / 0.116 hrs = ~8.1M RUs/s
└─ Peak: ~8.1M RUs/s >> Table limit (20k RUs/s) ❌ THROTTLE!

SEM rate limiting (problema):
└─ Servidor rejeitará com 429 Too Many Requests

COM rate limiting hierárquico (solução):
├─ Per-partition: 2k ops/s → 6.4k ops/s (32 partitions)
├─ Global: 20k ops/s → 20k ops/s (Table limit)
├─ Spread time: 5M / 20k = 250s = ~4 minutos ✓
└─ Throughput consumido: Respeitado ao máximo
```

### Batch Sizing: Trade-off Throughput vs Latency

```
Batch Size Analysis (100 itens por batch):

Com 100 items/batch:
├─ Batches necessários: 5M / 100 = 50,000 batches
├─ Batch request overhead: ~200 bytes
├─ Total overhead: 50k * 200 = 10 MB
├─ Latência por batch: ~50ms (HTTP RTT)
├─ Total time: 50k batches * 50ms = 2500s (SEQUENTIAL) ❌

Parallelizado (32 partitions, 20 concurrent):
├─ Batches por partição: 50k / 32 = 1,562 batches
├─ Concurrent batches: min(1562, 20 concurrent limit) = 20
├─ Throughput: 20 * 100 items / 50ms = 40k items/s
├─ Time: 5M / 40k = 125s ✓
└─ Eficiente!

vs. Batch Size = 1000 (maior):
├─ Batches: 5,000 (5x menor)
├─ Latência por batch: ~100ms (network overhead)
├─ Concurrent: 20
├─ Throughput: 20 * 1000 items / 100ms = 200k items/s
├─ Time: 5M / 200k = 25s ✓✓ (mais rápido!)
└─ Trade-off: mais memória (~40MB vs ~4MB)

Recomendação: 100 é bom balanço para production (segurança + performance)
```

### Monitoramento de Quota em Tempo Real

```kusto
// Application Insights query para monitorar consumo de quota

// 1. Taxas de requisição (para alertar se próximo a 20k/s)
customMetrics
| where name == "TableStorageRequests.Count"
| summarize requests_per_sec = count() / (max(timestamp) - min(timestamp)) 
           by tostring(customDimensions.PartitionKey)
| where requests_per_sec > 15000  // 75% do limite

// 2. Taxas de erro 429 (throttle)
customMetrics
| where name == "TableStorageError429.Count"
| summarize throttle_count = sum(value) by bin(timestamp, 1m)
| where throttle_count > 0

// 3. Latência de operação (indicador de contention)
customMetrics
| where name == "TableStorageLatency.Milliseconds"
| summarize avg_latency = avg(value), p99_latency = percentile(value, 99)
           by bin(timestamp, 5m)

// 4. Data written per partition
customMetrics
| where name == "TableStorageData.BytesWritten"
| summarize total_bytes = sum(value)
           by tostring(customDimensions.PartitionKey)
```

### Alertas Recomendados

| Métrica | Threshold | Ação |
|---------|-----------|------|
| **429 Errors** | > 5/min | ⚠️ Reduzir `MaxGlobalOperationsPerSecond` (20k → 15k) |
| **Request Rate** | > 18k/s | ⚠️ Aumentar `MaxRetries` (backoff mais agressivo) |
| **Partition Hot-spot** | Variance > 30% | ⚠️ Revisar estratégia de particionamento (hash distribution) |
| **Latency P99** | > 1000ms | ⚠️ Verificar capacity (scale up, RU aumentadas) |
| **Storage Size** | > 90% quota | ⚠️ Planejar archival/retention (deletar dados antigos) |

### Otimizações Avançadas

#### 1. **Reuse Table Rows (Update em vez de Insert)**
```
Se Tranco muda incrementalmente (removals < 1%):
├─ V1 (Insert only): 5M inserts
├─ V2 (Update existing): 50k updates + 5M inserts = 5.05M ops (1% economia)
└─ Benefício: pequeno neste caso, mas considerável em updates parciais
```

#### 2. **Batch Deletes de Dados Obsoletos**
```
Retenção de 90 dias:
├─ Cron: Daily @ 03:00 UTC
├─ Delete: domains com Timestamp < (now - 90 days)
├─ Batch delete: até 100 entities
└─ Throughput: similar ao import (20k ops/s limite)

Exemplo:
  dia 1 (5M insert) → 5M storage
  dia 2 (5M insert, 0 delete) → 10M storage
  ...
  dia 90 (5M insert, 0 delete) → 450M storage
  dia 91 (5M insert, 5M delete) → 450M storage (steady state)
```

#### 3. **Compression em RowKey para Reduzir I/O**
```
Atual: RowKey = "example.com" (~11 bytes)
Otimizado: RowKey = base36(hash) (~8 bytes)
└─ Economia: 5M * 3 bytes = 15 MB (negligível)

Não recomendado: readability loss > benefício
```

### Storage Account Quotas

```
Standard Storage Account (default):
├─ Max capacity: 500 TB (per account)
├─ Max requests: 20k RUs/s (per partition)
├─ Max entities: Unlimited
└─ Cost: Pay-as-you-go (data + transactions)

Com 5M * 178 bytes = ~890 MB:
├─ Storage cost: ~$0.02/mês (negligível)
├─ Transaction cost: ~$0.50/mês (50M ops @ $0.01 per 10k ops)
└─ Total: ~$0.52/mês (muito barato!)

Crescimento (10 anos, 5M/dia):
├─ Storage: 5M * 365 * 10 * 178 bytes = 3.25 TB (< 500 TB limit ✓)
├─ Archival: Delete após 90 dias (rolling window)
└─ Cost: Contínuo ~$0.50/mês
```

---

## Monitoramento & Observabilidade

### Logs Estruturados

```
[2024-01-15 02:00:00] INF Import Pipeline started (Schedule: Daily @ 02:00 UTC)
[2024-01-15 02:00:15] INF Tranco_TopDomains: Downloaded 4,800,000 items (78 MB)
[2024-01-15 02:00:30] INF Tranco_TopDomains: Validated 4,750,000 items (1.0% duplicates)
[2024-01-15 02:00:35] INF Hagezi_Blocklists: Downloaded 250,000 items
[2024-01-15 02:00:40] INF Hagezi_Blocklists: Validated 248,500 items (0.6% duplicates)
[2024-01-15 02:00:45] INF [Adaptive] Initial parallelism: 25, Partitions: 32
[2024-01-15 02:00:50] INF Partition distribution: ~153k items/partition (balanced)
[2024-01-15 02:01:00] DBG Batch 1: 100 items → partition_00 → 5ms
[2024-01-15 02:01:05] DBG Batch 2: 100 items → partition_01 → 5ms
[2024-01-15 02:01:10] WRN [Adaptive] ⚠ Timeout on partition_05! Reducing parallelism: 25 → 24
[2024-01-15 02:02:00] INF [Throttle] Rate: 18.5k ops/s (approaching 20k limit)
[2024-01-15 02:04:30] INF ✓ All partitions processed: 4.998M items in 270s (~18.5k items/s)
[2024-01-15 02:04:35] INF [Quota] Storage consumed: ~890 MB, Requests: ~5M, Cost: ~$0.50
[2024-01-15 02:04:40] INF Checkpoint recorded: Tranco @ 2024-01-15 02:04:30, Hagezi @ 2024-01-15 02:04:35
[2024-01-15 02:04:45] INF ✓ Import Pipeline completed successfully
```

### Application Insights Queries

```kusto
// Métrica: Taxa de processamento por segundo (deve estar próximo a 20k ops/s limite)
customMetrics
| where name == "ImportMetrics.ItemsProcessed"
| summarize count=sum(value) by bin(timestamp, 10s)
| extend ops_per_sec = count / 10

// Erros 429 (throttle) - alertar se > 0
customMetrics
| where name == "ImportMetrics.Error429Count"
| summarize errors=sum(value) by tostring(customDimensions.PartitionKey)

// Latência de operação por partição
customMetrics
| where name == "ImportMetrics.PartitionLatency"
| summarize avg_ms=avg(value), p99_ms=percentile(value, 99)
           by tostring(customDimensions.PartitionKey)

// Distribuição de carga (verificar se uniforme)
customMetrics
| where name == "ImportMetrics.PartitionItemCount"
| summarize items=sum(value) by tostring(customDimensions.PartitionKey)
| summarize avg_items=avg(items), variance=stdev(items)

// Consumo de quota (storage bytes + transactions)
customMetrics
| where name == "ImportMetrics.StorageConsumed"
| summarize total_mb=sum(value) by bin(timestamp, 1h)
```

### Health Check Endpoints

Embora o Importer seja one-shot, registra seu status:
```json
{
  "status": "completed",
  "duration_seconds": 270,
  "items_processed": 4998500,
  "items_failed": 1500,
  "success_rate": 0.9997,
  "final_parallelism": 24,
  "partition_distribution": {
    "min_items": 150000,
    "max_items": 157000,
    "variance": "4.2%"
  },
  "table_storage": {
    "storage_consumed_mb": 890,
    "requests": 50000,
    "cost_estimate": 0.50,
    "throttle_events": 0
  }
}
```

---

## Tratamento de Erros & Recovery

### Estratégias por Tipo de Erro

| Erro | Estratégia | Exemplo |
|------|-----------|---------|
| **Timeout (> 30s)** | Exponential backoff per partition | 100ms → 200ms → 400ms... |
| **429 (Rate Limit)** | Global slowdown + per-partition reduction | Reduce all partitions by 5% |
| **404 (Lista não existe)** | Skip + Log | Continue to next list |
| **Partial batch failure** | Retry individual items | 3 tentativas máximo |
| **Checkpoint error** | Log + Continue | Progress saved in next run |

### Circuit Breaker Implícito

```
consecutiveTimeouts > 10:
  ├─ Log warning
  ├─ Reduce parallelism to minimum (5)
  └─ Exponential backoff até 5s
```

---

## Deployment

### Local Development

```bash
# Via Docker
docker build -t nextdnsblocker-importer:latest .
docker run --rm \
  -e "AzureWebJobsStorage=DefaultEndpointsProtocol=https;..." \
  -e "ASPNETCORE_ENVIRONMENT=Development" \
  nextdnsblocker-importer:latest

# Via dotnet CLI
dotnet run --project src/NextDnsBetBlocker.Worker.Importer
```

### Azure Container Instances (Production)

```bash
az container create \
  --resource-group rg-dnsblocker \
  --name importer-daily \
  --image acr.azurecr.io/nextdnsblocker-importer:latest \
  --cpu 2 \
  --memory 1 \
  --restart-policy Never
```

### Agendamento (Azure Container Apps / Logic Apps)

```yaml
Schedule: Daily @ 02:00 UTC
├─ Baixa carga de rede
├─ Fora de horário de pico dos workers
└─ Margem antes de Worker consumir dados (06:00)
```

---

## Trade-offs & Decisões de Design

### ✅ Por que Particionamento?

**Alternativa**: Uma fila única para todos os domínios
- ✗ Contention em lock (um channel, muitos threads)
- ✗ Backoff global (falha em 1 partição afeta todas)
- ✗ Memória: canal único com 5M items

**Nossa abordagem**: 32 partições independentes (hash-based)
- ✓ Isolamento: falha local não afeta globais
- ✓ Escalabilidade: menos contention
- ✓ Memória: distribuído (5M / 32 = ~156k items/partition)
- ✓ Table Storage: evita hot-spots, distribui carga uniformemente

### ✅ Por que Batch Size = 100?

**Alternativa 1**: Batch size = 1 (insert individual)
- ✗ 50M HTTP requests (vs 500k com batch)
- ✗ Overhead de connection setup (5-10x mais lento)
- ✗ Custo: 10x mais ops contabilizadas

**Alternativa 2**: Batch size = 1000
- ✓ 5k requests (vs 500k)
- ✗ Memória: 1k items * 32 partitions = 32k em buffer
- ✗ Latência: ~100ms por batch (vs ~50ms com 100)
- ✗ Network: possível timeout se item lento

**Nossa abordagem**: Batch size = 100
- ✓ 500k requests (bom balanço)
- ✓ Memória: 100 * 32 = 3.2k items (negligível)
- ✓ Latência: ~50ms por batch
- ✓ Resiliência: retry mais granular

### ✅ Por que Adaptive Parallelism?

**Alternativa**: Fixed parallelism (ex: sempre 25)
- ✗ Muito agressivo em rede lenta → timeouts
- ✗ Muito conservador → subutilização de recursos
- ✗ Sem auto-recovery quando pressão reduz

**Nossa abordagem**: Começa alto, reduz 5% em timeout
- ✓ Auto-tuning sem configuração manual
- ✓ Recuperação automática quando pressão reduz
- ✓ Adaptável a diferentes ambientes (local, ACI, cloud)

### ✅ Por que Producer-Consumer?

**Alternativa**: Processamento síncrono (parse + store em série)
- ✗ CPU bloqueia esperando I/O (HTTP latency 50-200ms)
- ✗ Throughput reduzido 5-10x
- ✗ Sem pipelining

**Nossa abordagem**: Canais desacoplam produtor (parse) de consumidor (HTTP)
- ✓ Máxima utilização de I/O (pipelining)
- ✓ CPU nunca bloqueia no I/O
- ✓ Backpressure automática (Channel bounded)

### ✅ Por que Rate Limiting Hierárquico?

**Alternativa 1**: Sem rate limiting (agressivo)
- ✗ Gera 429 erros do Table Storage
- ✗ Retry exponencial → execução 2-3x mais lenta
- ✗ Quota exceeded (bad reputation)

**Alternativa 2**: Rate limit único (20k ops/s global)
- ✗ Falha em 1 partição afeta todas (throttle global)
- ✗ Menos flexível

**Nossa abordagem**: Per-partition (2k) + Global (20k)
- ✓ Isolamento: falha local não afeta globais
- ✓ Respeita limites do Table Storage
- ✓ Throughput máximo (próximo ao limite)
- ✓ Recuperação graceful

---

## Conclusão

O Importer demonstra **padrões enterprise-grade** para bulk data import em ambientes cloud:

- **Robustez**: Graceful degradation por partição, checkpointing, retry automático
- **Performance**: Adaptive parallelism + rate limiting hierárquico + pipelining
- **Escalabilidade**: Arquitetura agnóstica ao volume (1M, 5M, 100M domínios)
- **Observabilidade**: Logging estruturado, Application Insights, quota monitoring
- **Eficiência**: Operação próxima ao máximo de throughput do Table Storage (20k ops/s)

Perfeito para um **portfólio técnico** que demonstra domínio de:
- Padrões de distribuição (hash-based partitioning)
- Concorrência (Channels, SemaphoreSlim, async/await)
- Cloud economics (quota management, cost optimization)
- Resilience engineering (graceful degradation, adaptive patterns)
// Distribuição de carga (verificar se uniforme)
customMetrics
| where name == "ImportMetrics.PartitionItemCount"
| summarize items=sum(value) by tostring(customDimensions.PartitionKey)
| summarize avg_items=avg(items), variance=stdev(items)

// Consumo de quota (storage bytes + transactions)
customMetrics
| where name == "ImportMetrics.StorageConsumed"
| summarize total_mb=sum(value) by bin(timestamp, 1h)
```

### Health Check Endpoints

Embora o Importer seja one-shot, registra seu status:
```json
{
  "status": "completed",
  "duration_seconds": 95,
  "items_processed": 248500,
  "items_failed": 50,
  "success_rate": 0.998,
  "final_parallelism": 18
}
```

---

## Tratamento de Erros & Recovery

### Estratégias por Tipo de Erro

| Erro | Estratégia | Exemplo |
|------|-----------|---------|
| **Timeout (> 30s)** | Exponential backoff per partition | 100ms → 200ms → 400ms... |
| **429 (Rate Limit)** | Global slowdown + per-partition reduction | Reduce all partitions by 5% |
| **404 (Lista não existe)** | Skip + Log | Continue to next list |
| **Partial batch failure** | Retry individual items | 3 tentativas máximo |
| **Checkpoint error** | Log + Continue | Progress saved in next run |

### Circuit Breaker Implícito

```
consecutiveTimeouts > 10:
  ├─ Log warning
  ├─ Reduce parallelism to minimum (5)
  └─ Exponential backoff até 5s
```

---

## Deployment

### Local Development

```bash
# Via Docker
docker build -t nextdnsblocker-importer:latest .
docker run --rm \
  -e "AzureWebJobsStorage=DefaultEndpointsProtocol=https;..." \
  -e "ASPNETCORE_ENVIRONMENT=Development" \
  nextdnsblocker-importer:latest

# Via dotnet CLI
dotnet run --project src/NextDnsBetBlocker.Worker.Importer
```

### Azure Container Instances (Production)

```bash
az container create \
  --resource-group rg-dnsblocker \
  --name importer-daily \
  --image acr.azurecr.io/nextdnsblocker-importer:latest \
  --cpu 2 \
  --memory 1 \
  --restart-policy Never
```

### Agendamento (Azure Container Apps / Logic Apps)

```yaml
Schedule: Daily @ 02:00 UTC
├─ Baixa carga de rede
├─ Fora de horário de pico dos workers
└─ Margem antes de Worker consumir dados (06:00)
```

---

## Trade-offs & Decisões de Design

### ✅ Por que Particionamento?

**Alternativa**: Uma fila única para todos os domínios
- ✗ Contention em lock (um channel, muitos threads)
- ✗ Backoff global (falha em 1 partição afeta todas)
- ✗ Memória: canal único com 1M items

**Nossa abordagem**: 32 partições independentes
- ✓ Isolamento: falha local não afeta globais
- ✓ Escalabilidade: menos contention
- ✓ Memória: distribuído (32 × 3.2k items)

### ✅ Por que Adaptive Parallelism?

**Alternativa**: Fixed parallelism (ex: sempre 20)
- ✗ Muito agressivo → timeouts em rede lenta
- ✗ Muito conservador → subutilização de recursos

**Nossa abordagem**: Começa alto, reduz sob pressão
- ✓ Auto-tuning sem configuração
- ✓ Recuperação automática quando pressão reduz

### ✅ Por que Producer-Consumer?

**Alternativa**: Processamento síncrono (parse + store em série)
- ✗ CPU bloqueia esperando I/O
- ✗ Throughput reduzido 5-10x

**Nossa abordagem**: Canais desacoplam produtor (parse) de consumidor (HTTP)
- ✓ Máxima utilização de I/O
- ✓ Pipelining natural

---

## Conclusão

O Importer demonstra **padrões enterprise-grade** para bulk data import em ambientes cloud:

- **Robustez**: Graceful degradation por partição
- **Performance**: Adaptive parallelism + rate limiting hierárquico
- **Observabilidade**: Logging estruturado e métricas
- **Escalabilidade**: Arquitetura agnóstica ao volume

Perfeito para um **portfólio técnico** que demonstra domínio de padrões modernos de engenharia!

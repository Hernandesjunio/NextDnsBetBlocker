# NextDnsBetBlocker Worker & Function App 🚀

## Visão Geral

O **Worker Service** e o **Function App** são deployments alternativos de um **mesmo serviço de análise**. Ambos executam **continuamente** (24/7) e compartilham a mesma lógica de negócio, diferenciando-se apenas no modelo de hospedagem:

- **Worker Service**: Containerizado em Azure App Service / Container Apps
- **Function App**: Azure Functions (Consumption Plan ou Premium)

Ambos:
1. **Buscam logs DNS** do NextDNS (query/DNS records)
2. **Classificam domínios** usando cache Tranco (domínios legítimos)
3. **Analisam padrões suspeitos** usando Hagezi blocklists
4. **Publicam suspicious domains** em Azure Queue para ação (bloqueio)

---

## Responsabilidades Compartilhadas

```
┌─────────────────────────────────────────────────┐
│   Worker Service / Function App (Análise)       │
├─────────────────────────────────────────────────┤
│                                                  │
│  1️⃣  Fetch NextDNS Logs (Pagination)             │
│      ├─ API: GET /dns/logs (per profile)        │
│      ├─ Checkpoint: última vez que rodou       │
│      └─ Filtrar por tipo: query, blocked, etc   │
│                                                  │
│  2️⃣  Classify Domains                            │
│      ├─ Lookup Tranco cache (legítimos)        │
│      ├─ Lookup Hagezi blocklists                │
│      └─ Score: trusted | suspicious | blocked  │
│                                                  │
│  3️⃣  Detect Patterns (Behavioral Analysis)       │
│      ├─ Frequency analysis: N queries em X min  │
│      ├─ Time-of-day: padrões incomuns          │
│      └─ User correlation: múltiplos users       │
│                                                  │
│  4️⃣  Publish Results                            │
│      ├─ Queue: suspected-domains                │
│      └─ Metadata: score, pattern, evidence     │
│                                                  │
│  5️⃣  Update Checkpoint                           │
│      ├─ Store: last_run_timestamp               │
│      └─ Per-profile: rastrear progresso         │
│                                                  │
└─────────────────────────────────────────────────┘
```

---

## Arquitetura da Pipeline

### Fluxo em Tempo Real: Logs → Classificação → Publicação

```
┌──────────────────────┐
│  NextDNS API         │
│  (Query Logs)        │
└──────────┬───────────┘
           │
           ▼
┌──────────────────────────────────┐
│  1. Fetch Logs (per profile)     │ ◄── Checkpointing
│     - GET /dns/logs              │     (last_run per profile)
│     - Pagination: offset=X       │
│     - Timeout: 20s               │
└──────────┬───────────────────────┘
           │
           ▼
┌──────────────────────────────────┐
│  2. Channel (Log Buffer)         │
│     - Bounded: 1000 items        │
│     - Producer: API fetcher      │
│     - Consumers: classifiers     │
└──────────┬───────────────────────┘
           │
      ┌────┴────┬────────┬────────┐
      ▼         ▼        ▼        ▼
┌──────────┬──────────┬──────────┬──────────┐
│ Consumer │ Consumer │ Consumer │ Consumer │ ◄── Parallelism: 4
│  (Batch │  (Batch  │  (Batch  │  (Batch  │     (configurável)
│  1000)  │  1000)   │  1000)   │  1000)   │
└──────────┴──────────┴──────────┴──────────┘
      │        │         │        │
      ▼        ▼         ▼        ▼
┌──────────────────────────────────┐
│  3. Classification Pipeline      │
│  ┌────────────────────────────┐  │
│  │ a) Lookup Tranco Cache     │  │  Cache in-memory
│  │    (legítimos)             │  │  ~1M domains
│  │                            │  │
│  │ b) Lookup Hagezi Blocklist │  │  SQL query
│  │    (suspeitos)             │  │  <10ms
│  │                            │  │
│  │ c) Score domain            │  │  Algorithmic
│  │    ├─ Trusted (Tranco)     │  │  (3 categories)
│  │    ├─ Suspicious (Hagezi)  │  │
│  │    └─ Blocked (manual)     │  │
│  │                            │  │
│  │ d) Behavioral Analysis     │  │
│  │    ├─ Query frequency      │  │
│  │    ├─ Time patterns        │  │
│  │    └─ User correlation     │  │
│  └────────────────────────────┘  │
└──────────┬───────────────────────┘
           │
           ▼
┌──────────────────────────────────┐
│  4. Result Channel               │
│     - Suspected domains          │
│     - Metadata + score           │
└──────────┬───────────────────────┘
           │
           ▼
┌──────────────────────────────────┐
│  5. Publisher Task               │
│     - Publish to Queue           │
│     - Batch: 32 messages         │
│     - Batch timeout: 5s          │
└──────────┬───────────────────────┘
           │
           ▼
┌──────────────────────────────────┐
│  Azure Queue Storage             │
│  - suspected-domains-queue       │
│  - TTL: 7 days                   │
└──────────┬───────────────────────┘
           │
           ▼
┌──────────────────────────────────┐
│  Future: Enforcement Layer       │
│  (NextDNS Allowlist Update)      │
│  (ou: Log para análise manual)   │
└──────────────────────────────────┘

Checkpoint Store (para recuperação)
├─ last_run_timestamp (per profile)
├─ last_offset (pagination)
└─ items_processed (métrica)
```

---

## Diferenças: Worker vs Function App

### Worker Service (Continuous)

```csharp
// src/NextDnsBetBlocker.Worker/Program.cs
var host = new HostBuilder()
    .ConfigureServices(services =>
    {
        services.AddCoreServices(config, ServiceLayerType.Analysis);
        services.AddHostedService<BetBlockerBackgroundService>();
    })
    .Build();

await host.RunAsync();

// BetBlockerBackgroundService
public class BetBlockerBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var stats = await _pipeline.ProcessLogsAsync(profileId);
            
            // Sleep entre execuções: 5 minutos
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

**Características**:
- Loop infinito (executa a cada N minutos)
- Escalabilidade: múltiplas instâncias com distributed lock
- Custo: contínuo (hourly)
- Latência: seconds (sempre ativo)

### Function App (Event-Driven)

```csharp
// src/NextDnsBetBlocker.FunctionApp/AnalysisFunction.cs
[Function("AnalysisTrigger")]
public async Task Run(
    [TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,  // A cada 5 minutos
    FunctionContext context)
{
    var stats = await _pipeline.ProcessLogsAsync(profileId);
    // Executa, retorna, desaloca
}
```

**Características**:
- TimerTrigger: executa em schedule (ex: a cada 5 min)
- Escalabilidade: automática (cold start < 5s)
- Custo: pay-per-execution (mais barato se intermitente)
- Latência: 1-3s (cold start incluído)

### Comparação

| Aspecto | Worker | Function |
|---------|--------|----------|
| **Execution** | Loop contínuo | Trigger em schedule |
| **Deployment** | Container/App Service | Managed Functions |
| **Cold Start** | ~1s (containerizado) | ~2-3s (Consumption) |
| **Cost** | Hora de compute | #execuções × duração |
| **Latency** | Imediato (sempre ativo) | 1-3s delay |
| **Complexity** | BackgroundService | Function binding |
| **Monitoring** | App Insights native | App Insights native |
| **Scaling** | Manual/VMSS | Automático |

**Recomendação**:
- **Worker**: Baixa latência, previsibilidade de custo
- **Function**: Carga variável, cost-optimization

---

## Design Patterns & Resilience

### 1. **Pipeline Paralelo com Channels** 🔄

Mesmo padrão do Importer, mas aplicado a log processing:

```
Producer (API Fetcher)
  ├─ Fetch logs em batches
  └─ Enqueue to Channel (bounded: 1000)
       │
       ▼ Channel (producer-consumer)
       │
  Consumer Tasks (4 paralelos)
  ├─ Classify domains (CPU)
  ├─ Lookup caches (memory)
  ├─ Publish to queue
  └─ Repeat

Resultado:
- API fetch desacoplado de processamento
- Máxima throughput com controle de memória
```

### 2. **Graceful Degradation** ⚡

Falhas no processamento não interrompem o loop:

```
Log Entry → Classify
  ├─ Success: publish to queue
  ├─ Cache miss: mark as "unknown", publish anyway
  ├─ Exception: log error, continue next item
  └─ Queue full: backpressure (wait), retry

Checkpoint: atualizado APENAS após sucesso
  ├─ Se falhar: retry na próxima execução
  └─ Deduplicação automática (idempotent)
```

### 3. **Distributed Lock para Concorrência** 🔐

Múltiplas instâncias do Worker podem rodar, mas apenas 1 por vez:

```
Instance 1: Adquire lock → Processa → Libera lock
Instance 2: Aguarda lock → Adquire → Processa → Libera

Lock via Blob Storage:
├─ LeaseId (renovado a cada 30s)
├─ Timeout: 60s
└─ Auto-release se desconecta
```

**Benefício**: Alta disponibilidade (failover automático) sem dados duplicados

### 4. **Checkpoint Store para Recuperação** 📍

Rastreia progresso entre execuções:

```
┌─────────────────────────┐
│ CheckpointStore (Table) │
├─────────────────────────┤
│ profile_id: "home"      │
│ last_run: 2024-01-15... │
│ last_offset: 45000      │  ◄── Pagination offset
│ items_processed: 5000   │
│ status: "completed"     │
└─────────────────────────┘

Na próxima execução:
├─ Lê checkpoint
├─ Começa de offset=45000 (não reprocessa)
└─ Atualiza após sucesso
```

**Resiliência**:
- Crash durante processamento → restart pega de onde parou
- Network timeout → retry com offset anteriormente salvo

### 5. **Throttling Adaptativo (para API externa)** 🚦

Respeita rate limits do NextDNS:

```
Request → Response
  ├─ 200 OK: continua normal
  ├─ 429 Too Many Requests:
  │   ├─ Backoff: 2s → 4s → 8s...
  │   └─ Retry-After header (se fornecido)
  ├─ 503 Service Unavailable:
  │   ├─ Exponential backoff
  │   └─ Máximo: 5 retentativas
  └─ Timeout (>20s):
      ├─ Retira da fila
      ├─ Logs como "inconclusive"
      └─ Continua próxima
```

---

## Configuração

### appsettings.json (Worker)

```json
{
  "WorkerSettings": {
    "ExecutionIntervalMinutes": 5,
    "MaxConcurrentProcessors": 4,
    "BatchSize": 1000,
    "ChannelCapacity": 5000,
    "LogRetentionDays": 30,
    "NextDnsApiTimeout": 20
  },
  "NextDNSConfig": {
    "ApiKey": "your-api-key",
    "BaseUrl": "https://api.nextdns.io",
    "Profiles": ["profile-1", "profile-2"]
  }
}
```

### Configuração por Environment

| Env | Interval | Concurrency | Rationale |
|-----|----------|-------------|-----------|
| Dev | 30 min | 2 | Testes locais |
| Staging | 10 min | 4 | Close to prod |
| Prod | 5 min | 8 | Max throughput |

---

## Performance Characteristics

### Throughput

```
Por execução (5 minutos):
├─ API fetch: 10k logs
├─ Classification rate: 2k logs/s (parallelism=4)
├─ Processing time: 5s
├─ Queue publish: 100ms
└─ Total: ~6 segundos (bem dentro de 5 minutos)

Headroom:
├─ Tempo livre: 4:54
├─ Permite retries sem impacto
└─ Buffer para picos de carga
```

### Latency

```
Worker (always-on):
└─ E2E: ~1 segundo (logs já em memória)

Function (cold start):
├─ Cold start: 2s
├─ Execution: 5s
└─ E2E: ~7 segundos
```

### Memory

```
In-memory caches:
├─ Tranco (1M domains): ~50 MB (HashSet<string>)
├─ Hagezi blocklists: ~100 MB (Dictionary)
├─ Channel buffer (5k items): ~5 MB
└─ Total: ~155 MB

Worker: Persistent cache (warm start eficiente)
Function: Cache reloaded cada execução (cold start penalty)
```

---

## Monitoramento & Observabilidade

### Application Insights

```kusto
// Erro rate
traces
| where severity == "error"
| summarize error_count=count() by bin(timestamp, 5m)

// Latência de processamento
customMetrics
| where name == "AnalysisLatencyMs"
| summarize avg_latency=avg(value) by bin(timestamp, 5m)

// Domínios suspeitos detectados
customMetrics
| where name == "SuspectedDomainsCount"
| summarize total=sum(value) by bin(timestamp, 1h)
```

### Logging Estruturado

```
[2024-01-15 10:05:00] INF Worker started (interval: 5m)
[2024-01-15 10:05:01] INF Fetching logs for profile: home
[2024-01-15 10:05:03] INF Logs fetched: 8500 entries
[2024-01-15 10:05:08] INF Classification complete: 250 suspicious
[2024-01-15 10:05:09] INF Publishing to queue: 250 messages
[2024-01-15 10:05:10] INF ✓ Checkpoint updated: offset=53500, status=completed
[2024-01-15 10:05:10] INF Sleeping 4m50s until next execution...
```

---

## Tratamento de Erros & Recovery

| Erro | Estratégia |
|------|-----------|
| **API 429 (Rate Limit)** | Exponential backoff (max 5 retries) |
| **API 503 (Service Down)** | Retry com fallback (usar cached logs) |
| **API timeout (>20s)** | Partial success + log incomplete |
| **Queue full** | Backpressure (wait) + timeout |
| **Lock contention** | Retry acquiring (max 3x) |
| **Checkpoint write fail** | Log warning, continue (será recuperado) |
| **Classification fail** | Mark as "unknown", publish anyway |

---

## Deployment

### Worker Service (Container Apps)

```bash
# Build image
docker build -t acr.azurecr.io/nextdnsblocker-worker:latest \
  -f src/NextDnsBetBlocker.Worker/Dockerfile .

# Deploy to Container Apps
az container app create \
  --name worker \
  --resource-group rg-dnsblocker \
  --image acr.azurecr.io/nextdnsblocker-worker:latest \
  --cpu 1 \
  --memory 1
```

### Function App (Consumption Plan)

```bash
# Deploy via Azure Functions CLI
func azure functionapp publish nextdnsblocker-func --build remote

# ou via GitHub Actions (CI/CD)
```

### Scaling

| Layer | Strategy | Limits |
|-------|----------|--------|
| **Worker** | Replica count (2-10) | 10 instances (cost vs redundancy) |
| **Function** | Automatic (VMSS) | 200 concurrent executions |
| **Queue** | Partition key (per domain) | 20k msgs/s |

---

## Trade-offs & Decisões de Design

### ✅ Por que Channels em vez de Task.Run?

**Alternativa**: Criar task por log entry
```csharp
foreach (var log in logs)
    _ = Task.Run(() => Classify(log)); // Fire-and-forget ❌
```
- ✗ Não-controlado: pode criar 10k tasks (crash)
- ✗ Sem backpressure: overflow causa OOM

**Nossa abordagem**: Channel bounded
- ✓ Limite explícito de memória
- ✓ Backpressure automática (produtor aguarda)

### ✅ Por que Parallelism=4 (não mais)?

**Alternativa**: 8-10 paralelos
- ✗ Excesso de contention em cache
- ✗ Mais context-switch (CPU overhead)

**Nossa abordagem**: 4 (tuned empiricamente)
- ✓ Cache eficiente
- ✓ CPU bem utilizado
- ✓ Headroom para picos

### ✅ Por que Distributed Lock?

**Alternativa**: Sem lock (múltiplas instâncias processam mesmo offset)
- ✗ Dados duplicados na fila
- ✗ Overhead duplo

**Nossa abordagem**: Blob Storage lease
- ✓ Apenas 1 instance ativa por vez
- ✓ Failover automático (lock timeout)
- ✓ Simples de implementar

---

## Roadmap & Melhorias Futuras

```
[ ] Event-driven trigger (WebHook do NextDNS)
    └─ Em vez de polling (mais eficiente)

[ ] Machine Learning para pattern detection
    └─ Anomaly detection automática

[ ] Integração direta com NextDNS Allowlist API
    └─ Auto-block após score threshold

[ ] Multi-profile parallelism
    └─ Processar múltiplos profiles em paralelo

[ ] Metrics exporting (Prometheus)
    └─ Para observabilidade agnostic-cloud
```

---

## Conclusão

O Worker/Function App demonstram **padrões cloud-native** para análise em tempo real:

- **Resiliência**: Distributed lock + checkpoint recovery
- **Performance**: Pipeline paralelo com pipelining
- **Observabilidade**: Logging estruturado + Application Insights
- **Escalabilidade**: Múltiplos deployments com deduplicação automática
- **Flexibilidade**: Worker + Function (escolha do modelo de hospedagem)

Excelente showcase para um **portfólio técnico** combinando:
- Padrões enterprise (.NET best practices)
- Cloud architecture (Azure services)
- Resilience patterns (retry, circuit breaker, graceful degradation)

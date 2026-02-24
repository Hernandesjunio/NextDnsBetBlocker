# Table Storage Operational Guide 📊

Um guia prático para operadores monitorarem e otimizarem o consumo de quota do Table Storage durante importações de 4.8M+ domínios.

## Quick Reference

### Azure Table Storage Limits (por partition key)
```
Throughput:     20,000 RU/s (eventual consistency)
Entity size:    1 MB máximo
Batch size:     100 entities máximo
Request rate:   ~20k requests/s per partition
Max storage:    500 TB per account
```

### NextDnsBetBlocker Import Profile
```
Dataset:        5M+ domínios (Tranco 4.8M + Hagezi listas)
Batch size:     100 items
Partitions:     32 (hash-based distribution)
Target rate:    18-20k ops/s (máximo seguro)
Expected time:  ~4-5 minutos
Storage:        ~890 MB (1x import)
Cost:           ~$0.50 (transaction cost)
```

---

## High-Throughput Architecture (Internals)

Para atingir a importação massiva com estabilidade, o sistema utiliza um conjunto de padrões de resiliência. Esta seção serve como referência para implementação de sistemas similares de alta vazão.

### 1. Hierarchical Token Bucket com Burst Control
O sistema limita a vazão em dois níveis simultâneos usando o algoritmo Token Bucket:
1. **Global Limit**: Protege a largura de banda da rede e CPU do container (ex: 20k ops/s).
2. **Partition Limit**: Protege partições individuais do Azure Table Storage (ex: 2k ops/s - limite físico da Azure).

**Correção de Burst**: Diferente de implementações ingênuas, o *Burst Capacity* (rajada permitida, geralmente 10% do rate) é recalculado dinamicamente. Se uma partição sofre degradação (ex: cai para 1000 ops/s), o burst é ajustado proporcionalmente (100 ops), evitando picos que casariam novos erros 429.

### 2. Backpressure (Contrapressão) via Bounded Channels
Para evitar *Out of Memory* (OOM) quando a escrita (Storage) é mais lenta que a leitura (Download/Parsing):
- Utilizamos `System.Threading.Channels` com capacidade limitada (`BoundedChannel`).
- Se o canal enche (ex: 500 batches na fila), o **Produtor (Parser)** é suspenso (`await WriteAsync`).
- Isso propaga a lentidão "para trás" até a origem, equilibrando o sistema sem descartar dados.

### 3. Adaptive Circuit Breaker com Step Recovery
Em vez de falhar ou tentar cegamente, o sistema monitora erros `429 Too Many Requests`:
1. **Degradação**: Ao encontrar erro, reduz o limite da partição (ex: -10%).
2. **Circuit Breaker**: Se a redução atingir o piso (ex: 50%), abre o circuito e para de enviar para aquela partição temporariamente.
3. **Step Recovery (Recuperação em Degraus)**: A recuperação **não é instantânea**. O sistema sobe o limite em pequenos degraus (ex: +10%) a cada intervalo (ex: 5s) se houver sucesso. Isso evita a oscilação ("flapping") entre carga total e erro.

---

## Pre-Import Checklist

### 1. Verificar Quotas Disponíveis
```kusto
// Query Application Insights
customMetrics
| where name == "ImportMetrics.StorageConsumed"
| summarize latest_mb=max(value) by bin(timestamp, 1h)
| tail 1
```

**Ação**: Se > 400GB acumulado:
- Executar archival job (delete dados > 90 dias)
- Ou provisionar novo storage account

### 2. Verificar Status do Storage Account
```bash
az storage account show \
  --name <storage_account> \
  --resource-group <rg> \
  --query "properties.{created:creationTime, primaryLocation, statusOfPrimary}"
```

**Ação**: Se status = "unavailable", aguardar ou failover

### 3. Limpar Cache Local (se necessário)
```bash
# Remover checkpoint de falha anterior (forçar re-import)
az storage table row delete \
  --account-name <storage> \
  --table-name Checkpoints \
  --partition-key ImportStatus \
  --row-key Tranco_TopDomains
```

---

## Durante o Import

### Monitoramento em Tempo Real

**Terminal 1: Taxa de processamento**
```kusto
customMetrics
| where name == "ImportMetrics.ItemsProcessed"
| summarize count=sum(value) by bin(timestamp, 10s)
| extend ops_per_sec = count / 10
| order by timestamp desc
```

**Terminal 2: Erros e throttles**
```kusto
customMetrics
| where name == "ImportMetrics.Error429Count"
| summarize errors=sum(value) by bin(timestamp, 1m)
| where errors > 0
```

**Terminal 3: Distribuição por partição**
```kusto
customMetrics
| where name == "ImportMetrics.PartitionItemCount"
| summarize items=sum(value) by tostring(customDimensions.PartitionKey)
| extend variance_pct = stdev(value) * 100 / avg(value)
```

### Alertas Red Flags

| Métrica | Normal | Atenção | Crítico |
|---------|--------|---------|---------|
| **Ops/s** | 18-20k | 15-18k | < 10k |
| **Error 429/min** | 0 | 1-5 | > 10 |
| **Partition variance** | < 5% | 5-15% | > 20% |
| **Latency P99** | < 100ms | 100-500ms | > 1000ms |

**Ações recomendadas:**

```
Se Error 429/min > 5:
  ├─ Aumentar `MaxRetries` (appsettings: 3 → 5)
  ├─ Reduzir `MaxGlobalOperationsPerSecond` (20k → 15k)
  └─ Monitor próximo ciclo

Se Ops/s < 10k:
  ├─ Verificar: network latency (ping storage account)
  ├─ Verificar: CPU/Memory do container
  └─ Se persistir: rollback a partition count (32 → 16)

Se Partition variance > 20%:
  ├─ Hash function pode estar ruim
  ├─ Fazer dump de um partition (10 items)
  └─ Verificar: distribuição de domínios
```

---

## Post-Import Validation

### Verificar Completude
```sql
-- SQL Query (após import via Data Explorer)
SELECT 
  PartitionKey,
  COUNT(*) as ItemCount
FROM DomainListsTable
WHERE ImportedDate = CAST(GETDATE() AS DATE)
GROUP BY PartitionKey
ORDER BY ItemCount DESC
```

**Esperado**: 32 partições, ~156k items cada, variance < 5%

### Verificar Integridade
```kusto
// Application Insights
customMetrics
| where name == "ImportMetrics.ValidationErrors"
| summarize errors=sum(value) by tostring(customDimensions.ErrorType)

// Alertar se > 0 duplicates após validation
```

**Ação**: Se erros > 0.1%:
- Revisar logs do GenericListImporter
- Verificar: source list integrity
- Rerun import

### Atualizar Checkpoint
```bash
# Via Azure Portal ou CLI
az storage table row merge \
  --account-name <storage> \
  --table-name Checkpoints \
  --partition-key ImportStatus \
  --row-key Tranco_TopDomains \
  --entity timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ) status=completed items=4998500
```

---

## Troubleshooting Comum

### Problema: 429 Too Many Requests

**Causa**: Exceeding 20k RU/s per partition

**Diagnóstico**:
```kusto
customMetrics
| where name == "ImportMetrics.Error429Count"
| summarize errors=sum(value), ops=sum(value)*20
| extend inferred_ops_per_sec = ops / 300  // 5 min window
```

**Solução**:
1. **Imediato**: Aumentar retry delay (exponential backoff)
2. **Médio prazo**: Reduzir parallelism (InitialDegreeOfParallelism: 25 → 20)
3. **Longo prazo**: Premium Table Storage (10k RU/s provisioned)

### Problema: Timeout no Import

**Causa**: Network latency ou Container lento

**Diagnóstico**:
```bash
# Testar latência de rede
ping blob.core.windows.net  # deve ser < 50ms
```

**Solução**:
1. Aumentar timeout (appsettings: 30s → 60s)
2. Reduzir batch size (100 → 50)
3. Aumentar container memory (1 GB → 2 GB)

### Problema: Partition Hot-Spot

**Causa**: Hash distribution ruim (ex: muitos domínios com mesmo prefixo).

**Sintoma**: Logs `⚠️ Partition {X} throughput warning...` frequentes em uma única partição enquanto outras estão ociosas.

**Diagnóstico**:
```kusto
customMetrics
| where name == "ImportMetrics.PartitionItemCount"
| summarize items=sum(value) by PartitionKey
| extend variance = stdev(items) * 100 / avg(items)
| where variance > 20
```

**Solução**:
1. Aumentar partition count (32 → 64).
2. O sistema agora inclui **Burst Control Dinâmico** que mitiga parcialmente picos curtos, mas hotspots sustentados exigem re-particionamento.

---

## Logs e Monitoramento de Throttling

O sistema emite novos logs específicos para saúde de vazão:

- **Aviso (`Warning`)**: `Partition {X} throughput warning: 2200 items/sec (Limit: 2000)`
    - *Significado*: A partição excedeu o limite nominal. O Burst Bucket absorveu o excesso temporariamente.
    - *Ação*: Se for esporádico, ignorar. Se contínuo, indica configuração de `MaxConcurrency` muito agressiva.

- **Informação (`Info`)**: `Partition {X} recovering. Limit restored to 1800 ops/sec`
    - *Significado*: O mecanismo de **Step Recovery** subiu um degrau de performance após período de estabilidade.
    - *Ação*: Nenhuma. Indica que o sistema está se auto-curando.

- **Erro (`Error`)**: `Import completed with issues | Degradation events...`
    - *Significado*: Resumo final indicando que houve gargalos.
    - *Ação*: Verificar métrica `MaxThroughput` no relatório. Se estiver muito acima do limite (ex: 3000 em limite de 2000), ajustes de throttling são necessários.

---

## Cost Monitoring

### Consumo Esperado (por import)

```
Storage cost:      ~$0.02  (890 MB a $0.018/GB)
Transaction cost:  ~$0.50  (50M ops a $0.01 per 10k ops)
Total per day:     ~$0.52
Monthly:           ~$15.60
Yearly:            ~$187.20
```

### Como Reduzir Custo

| Técnica | Economia | Tradeoff |
|---------|----------|----------|
| **Batch size: 100 → 1000** | ~10x | Latency +100%, memory +10x |
| **Partition count: 32 → 16** | ~2x (contention) | Risk de hot-spots |
| **Retention: 90d → 30d** | ~3x | Menos histórico |
| **Delete archival daily** | ~1x | Operational complexity |

**Recomendado**: Kombiniert:
- Batch size = 100 (manter segurança)
- 30-day retention (vs 90-day)
- Daily delete de dados antigos

---

## Optimization Playbook

### Fase 1: Baseline (Primeira execução)
```
Objetivo: Entender características reais
├─ Execute com config padrão
├─ Registre: throughput, errors, latency, cost
└─ Documente: variações vs expectativa
```

### Fase 2: Tune (Iterativo)
```
Se Ops/s < 15k:
  ├─ Aumentar InitialDegreeOfParallelism (20 → 30)
  ├─ Aumentar MaxGlobalOperationsPerSecond (20k → 25k)
  └─ Rerun

Se Error 429 > 5/min:
  ├─ Reduzir MaxGlobalOperationsPerSecond (20k → 15k)
  ├─ Aumentar MaxRetries (3 → 5)
  └─ Rerun
```

### Fase 3: Production (Estável)
```
Objetivo: Operação confiável com alertas
├─ Lock configuração após 3 successful runs
├─ Setup alertas (429 errors, variance, latency)
├─ Schedule: Daily @ 02:00 UTC
└─ Automático + manual oversight
```

---

## SLA & Health Metrics

### Target Metrics
```
Throughput:  18-20k ops/s
Latency:     < 100ms (p99)
Success:     > 99.9% (< 5k errors em 5M)
429 errors:  < 10/min (ideal: 0)
Variance:    < 10% (partitions balanceados)
Cost:        < $1/dia (target: ~$0.50)
```

### Dashboarding (Application Insights)

```json
{
  "name": "Import Monitoring",
  "tiles": [
    {
      "title": "Ops/sec",
      "metric": "ImportMetrics.ItemsProcessed",
      "aggregation": "sum",
      "timespan": "10s",
      "threshold": [15000, 20000]
    },
    {
      "title": "Error 429",
      "metric": "ImportMetrics.Error429Count",
      "aggregation": "sum",
      "timespan": "1m",
      "threshold": [0, 10]
    },
    {
      "title": "Partition Variance",
      "metric": "ImportMetrics.PartitionItemCount",
      "aggregation": "stdev",
      "threshold": ["< 5%", "> 20%"]
    }
  ]
}
```

---

## Contato & Escalation

**Observado comportamento anômalo?**

1. **Coletar logs**: Export Application Insights logs (últimas 2 horas)
2. **Documentar**: 
   - Horário do evento
   - Métrica afetada
   - Screenshot do dashboard
3. **Verificar**: Consultar Azure status page (status.azure.com)
4. **Reportar**: Abrir issue no GitHub com logs + diagnosis

---

**Última atualização**: 2024-01-15  
**Versão**: 1.0 (para 4.8M Tranco + Hagezi)

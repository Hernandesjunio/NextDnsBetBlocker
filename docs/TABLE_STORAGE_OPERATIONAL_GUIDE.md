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

**Causa**: Hash distribution ruim (ex: muitos domínios com mesmo prefixo)

**Diagnóstico**:
```kusto
customMetrics
| where name == "ImportMetrics.PartitionItemCount"
| summarize items=sum(value) by PartitionKey
| extend variance = stdev(items) * 100 / avg(items)
| where variance > 20
```

**Solução**:
1. Aumentar partition count (32 → 64)
2. Usar hash différente (MD5 → SHA256)
3. Pre-processor: shuffle items antes de partition

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

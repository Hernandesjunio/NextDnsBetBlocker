# Documentation Index & Navigation Guide 📚

Navegação completa da documentação do NextDnsBetBlocker com foco em Table Storage Efficiency para importação de 4.8M+ domínios.

---

## 📑 Quick Navigation

### Por Persona

#### 👨‍💼 **Product Manager / Stakeholder**
1. Comece com: [README.md](../README.md) - Visão geral da solução
2. Depois: [COST_ANALYSIS.md](COST_ANALYSIS.md) - Business metrics e ROI
3. Referência: [TABLE_STORAGE_OPERATIONAL_GUIDE.md](TABLE_STORAGE_OPERATIONAL_GUIDE.md) - Operational health

#### 👨‍💻 **Software Engineer / Architect**
1. Comece com: [README.md](../README.md) - Architecture overview
2. Deep-dive: [IMPORTER_README.md](IMPORTER_README.md) - Design patterns, quota management
3. Deep-dive: [WORKER_FUNCTION_README.md](WORKER_FUNCTION_README.md) - Analysis pipeline
4. Patterns: [HIGH_THROUGHPUT_PATTERNS.md](HIGH_THROUGHPUT_PATTERNS.md) - Reusable code patterns
5. Reference: [COST_ANALYSIS.md](COST_ANALYSIS.md) - Economics of design decisions

#### 👷 **DevOps / Operations**
1. Quick start: [TABLE_STORAGE_OPERATIONAL_GUIDE.md](TABLE_STORAGE_OPERATIONAL_GUIDE.md) - Checklist, monitoring
2. Setup: [AZURE_DEPLOYMENT_GUIDE.md](AZURE_DEPLOYMENT_GUIDE.md) - Infrastructure provisioning
3. Automation: [CI-CD_README.md](CI-CD_README.md) - Build and deploy pipelines
4. Tuning: [IMPORTER_README.md](IMPORTER_README.md) § "Configuration" - Performance tuning

#### 💰 **Finance / Business Analyst**
1. Start: [COST_ANALYSIS.md](COST_ANALYSIS.md) - Cost breakdown and projections
2. Context: [IMPORTER_README.md](IMPORTER_README.md) § "Azure Table Storage Optimization" - What drives costs
3. Benchmark: [TABLE_STORAGE_OPERATIONAL_GUIDE.md](TABLE_STORAGE_OPERATIONAL_GUIDE.md) § "Cost Monitoring" - How to track

---

## 📖 Complete Documentation Map

```
NextDnsBetBlocker Documentation
│
├─ 🎯 README.md (ENTRY POINT)
│  ├─ Solution overview
│  ├─ Architecture diagram
│  ├─ Technology stack
│  ├─ 3 main components (Importer, Worker, Function)
│  └─ Quick start & deployment overview
│
├─ 📥 IMPORTER_README.md (DETAILED TECHNICAL)
│  ├─ Responsibilities (6-step pipeline)
│  ├─ Architecture diagram (detailed)
│  ├─ Design Patterns:
│  │  ├─ Adaptive Parallelism (5-30 tasks)
│  │  ├─ Hierarchical Throttling (per-partition + global)
│  │  ├─ Graceful Degradation (partition isolation)
│  │  ├─ Distributed Batch Isolation (32 partitions)
│  │  └─ Producer-Consumer Pipelining
│  │
│  ├─ Configuration (appsettings, tuning by environment)
│  ├─ Performance Characteristics
│  │  ├─ Throughput (4-5 min for 5M items)
│  │  ├─ Memory (<400 MB)
│  │  └─ CPU (I/O bound)
│  │
│  ├─ 🔐 Azure Table Storage Optimization & Quota Management
│  │  ├─ Partition strategy (hash-based, 32 partitions)
│  │  ├─ Throughput calculation (RUs, TUs)
│  │  ├─ Batch sizing trade-offs (100 → 1000)
│  │  ├─ Monitoring queries (Application Insights)
│  │  ├─ Alert thresholds
│  │  ├─ Advanced optimizations (batch deletes, compression)
│  │  └─ Storage quotas & cost (10 years projection)
│  │
│  ├─ Monitoring & Observability
│  │  ├─ Structured logs (5M items/day scenario)
│  │  ├─ Application Insights queries (quota-focused)
│  │  └─ Health check endpoints
│  │
│  ├─ Error Handling & Recovery
│  ├─ Deployment (local, ACI, scheduling)
│  │
│  ├─ Trade-offs & Design Decisions
│  │  ├─ Why Partitioning? (hash-based vs single queue)
│  │  ├─ Why Batch Size 100? (vs 1, 1000)
│  │  ├─ Why Adaptive Parallelism? (vs fixed)
│  │  ├─ Why Producer-Consumer? (vs sync)
│  │  └─ Why Hierarchical Throttling? (isolation benefit)
│  │
│  └─ Complete Documentation Ecosystem (this map)
│
├─ 🚀 WORKER_FUNCTION_README.md
│  ├─ Shared responsibilities (Worker + Function App)
│  ├─ Architecture diagram (pipeline)
│  ├─ Worker vs Function comparison
│  ├─ Design patterns:
│  │  ├─ Pipeline Paralelo com Channels
│  │  ├─ Graceful Degradation
│  │  ├─ Distributed Lock
│  │  ├─ Checkpoint Store
│  │  └─ Throttling Adaptativo
│  ├─ Performance characteristics
│  ├─ Deployment & scaling
│  └─ Trade-offs analysis
│
├─ 🛠️ TABLE_STORAGE_OPERATIONAL_GUIDE.md (OPERATIONS PLAYBOOK)
│  ├─ Quick Reference (limits, profile)
│  ├─ Pre-Import Checklist
│  │  ├─ Quota verification
│  │  ├─ Status check
│  │  └─ Cache cleanup
│  │
│  ├─ Durante Import
│  │  ├─ Real-time monitoring (3 terminals)
│  │  ├─ Alert thresholds (ops/s, 429 errors, variance)
│  │  └─ Remediation actions
│  │
│  ├─ Post-Import Validation
│  │  ├─ Completeness check (SQL query)
│  │  ├─ Integrity check
│  │  └─ Checkpoint update
│  │
│  ├─ Troubleshooting Playbook
│  │  ├─ 429 Too Many Requests (diagnosis & fix)
│  │  ├─ Timeout in Import (diagnosis & fix)
│  │  └─ Partition Hot-Spot (diagnosis & fix)
│  │
│  ├─ Cost Monitoring
│  │  ├─ Expected consumption
│  │  └─ Optimization techniques (quick wins)
│  │
│  ├─ Optimization Playbook (3 phases)
│  │  ├─ Phase 1: Baseline
│  │  ├─ Phase 2: Tune
│  │  └─ Phase 3: Production
│  │
│  ├─ SLA & Health Metrics
│  │  ├─ Target metrics
│  │  └─ Dashboard setup
│  │
│  └─ Escalation procedures
│
├─ ⚙️ THROTTLING_IMPROVEMENTS.md (TECHNICAL FIXES)
│  ├─ Problem Identified
│  │  ├─ Burst rate desincronizado com effective rate
│  │  ├─ Overhead: 11.1% - 12.3% (deveria ser 10%)
│  │  └─ Impacto: Picos irregulares e comportamento imprevisível
│  │
│  ├─ Solution Implemented
│  │  ├─ Sincronização explícita: if (partitionBucket.Rate != effectiveLimit)
│  │  ├─ Recalc automático ao degradar
│  │  └─ TokenBucket recriado com burst correto
│  │
│  ├─ Validation & Testing
│  │  ├─ 9 testes cobrindo todos cenários
│  │  ├─ 100% burst accuracy (antes: 0-123%)
│  │  └─ 93% redução em variabilidade
│  │
│  ├─ Deployment
│  │  ├─ 100% backward compatible
│  │  ├─ Nenhuma reconfiguração necessária
│  │  ├─ Checklist pós-deploy
│  │  └─ Impacto: ~6-8min import, comportamento estável
│  │
│  └─ FAQ & Próximos passos
│
├─ 💰 COST_ANALYSIS.md
│  ├─ Cost Model
│  │  ├─ Storage cost (~$0.003/mo)
│  │  ├─ Transaction cost (~$1.50/mo @ batch 100)
│  │  ├─ Transfer cost (negligible)
│  │  └─ Premium RU cost (not recommended)
│  │
│  ├─ Scenarios (4 variations)
│  │  ├─ Standard - Batch Import (CURRENT: $1.50/mo)
│  │  ├─ Standard - Aggressive (High Risk: $1.11/mo, 10% errors)
│  │  ├─ Premium - Ultra-Low Latency ($9,000/mo - OVERKILL)
│  │  └─ Archive Strategy - 30 day retention ($0.54/mo, 31% savings)
│  │
│  ├─ Trade-offs by Configuration
│  │  ├─ Batch size impact (100 vs 500 vs 1000)
│  │  ├─ Partition count impact (16 vs 32 vs 64)
│  │  └─ Frequency impact (1x vs 2x vs 4x daily)
│  │
│  ├─ Optimization Opportunities (4 listed)
│  │  ├─ 1. Batch size 100→500: 80% cost reduction (IMPLEMENT)
│  │  ├─ 2. Retention 90d→30d: 31% savings (IF POLICY ALLOWS)
│  │  ├─ 3. Bloom filter dedup: 1% savings (NOT WORTH IT)
│  │  └─ 4. Premium tier: NOT RECOMMENDED
│  │
│  ├─ ROI Analysis
│  │  ├─ Batch size tuning: $25.92/hour ROI (DO NOW)
│  │  ├─ Retention automation: $23.82/hour ROI (NEXT SPRINT)
│  │  └─ Bloom filter: $0.035/hour (SKIP)
│  │
│  ├─ 3-Year Projections
│  │  ├─ Current trajectory: $118 total
│  │  └─ Optimized trajectory: $24 total (80% savings)
│  │
│  └─ Summary table & formulas
│
├─ 🌍 AZURE_DEPLOYMENT_GUIDE.md
│  ├─ Infrastructure setup
│  ├─ Configuration management
│  ├─ Security & compliance
│  ├─ Monitoring setup
│  └─ Disaster recovery
│
└─ 🚢 CI-CD_README.md
   ├─ Build pipelines
   ├─ Test automation
   ├─ Deployment workflows
   └─ Release process
```

---

## 🔍 Topic-Based Navigation

### **Table Storage Optimization & Efficiency**
The core technical topic for this project:

1. **Architecture Level**: [IMPORTER_README.md](IMPORTER_README.md) § "Azure Table Storage Optimization & Quota Management"
   - Hash partitioning strategy
   - Throughput calculation (RUs, TUs)
   - Batch sizing trade-offs
   - Advanced optimizations

2. **Operational Level**: [TABLE_STORAGE_OPERATIONAL_GUIDE.md](TABLE_STORAGE_OPERATIONAL_GUIDE.md)
   - Real-time monitoring
   - Troubleshooting (429 errors, hot-spots)
   - Cost tracking

3. **Economic Level**: [COST_ANALYSIS.md](COST_ANALYSIS.md)
   - Cost breakdown
   - Scenarios & trade-offs
   - ROI calculations

---

### **Design Patterns & Resilience**

**Adaptive Parallelism**:
- [IMPORTER_README.md](IMPORTER_README.md) § "Adaptive Parallelism 🎯"
- Real-world example: 30 → 24 after timeout

**Hierarchical Throttling**:
- [IMPORTER_README.md](IMPORTER_README.md) § "Hierarchical Throttling 🚦"
- Per-partition (2k ops/s) + global (20k ops/s)

**Graceful Degradation**:
- [IMPORTER_README.md](IMPORTER_README.md) § "Graceful Degradation - Por Partição ⚡"
- Partition isolation prevents global failure

**Distributed Batch Isolation**:
- [IMPORTER_README.md](IMPORTER_README.md) § "Distributed Batch Isolation 📦"
- 32 independent channels with own retry

**Producer-Consumer Pipelining**:
- [IMPORTER_README.md](IMPORTER_README.md) § "Producer-Consumer com Pipelining 🔄"
- Decouple parsing (CPU) from HTTP (I/O)

---

### **Performance & Scalability**

- [IMPORTER_README.md](IMPORTER_README.md) § "Performance Characteristics"
  - Throughput: 4-5 min for 5M items
  - Memory: <400 MB
  - CPU: I/O bound (5% utilization)

- [IMPORTER_README.md](IMPORTER_README.md) § "Azure Table Storage Optimization"
  - Batch sizing: 100 → 1000 analysis
  - Partition count scaling
  - Load distribution metrics

---

### **Monitoring & Observability**

**Application Insights Queries**:
- [IMPORTER_README.md](IMPORTER_README.md) § "Application Insights Queries"
- Focus: ops/s, 429 errors, latency, partition variance

**Health Check**:
- [IMPORTER_README.md](IMPORTER_README.md) § "Health Check Endpoints"
- Includes storage consumed, cost estimate

**Operational Monitoring**:
- [TABLE_STORAGE_OPERATIONAL_GUIDE.md](TABLE_STORAGE_OPERATIONAL_GUIDE.md) § "Durante Import"
- 3 real-time monitoring terminals
- Alert thresholds

---

### **Cost & Economics**

- **Cost Model**: [COST_ANALYSIS.md](COST_ANALYSIS.md) § "Modelo de Custo"
- **Scenarios**: [COST_ANALYSIS.md](COST_ANALYSIS.md) § "Cenários de Custo"
- **Optimizations**: [COST_ANALYSIS.md](COST_ANALYSIS.md) § "Optimization Opportunities"
- **3-Year Projections**: [COST_ANALYSIS.md](COST_ANALYSIS.md) § "Cost Projection"
- **ROI Analysis**: [COST_ANALYSIS.md](COST_ANALYSIS.md) § "ROI: Optimization Effort"

---

## 📊 Key Metrics Reference

### Azure Table Storage Limits
| Metric | Limit |
|--------|-------|
| Throughput | 20,000 RU/s per partition |
| Entity size | 1 MB max |
| Batch size | 100 entities max |
| Request rate | ~20k req/s per partition |
| Max storage | 500 TB per account |

### NextDnsBetBlocker Profile
| Metric | Value |
|--------|-------|
| Dataset | 5M+ (Tranco 4.8M + Hagezi) |
| Batch size | 100 items |
| Partitions | 32 (hash-based) |
| Target rate | 18-20k ops/s |
| Time to import | 4-5 minutes |
| Storage | ~890 MB |
| Cost | ~$0.50 transaction |

### Recommended Configuration
| Setting | Value | Range |
|---------|-------|-------|
| InitialDegreeOfParallelism | 25 | 5-30 |
| PartitionCount | 32 | 8-64 |
| BatchSize | 100 | 1-1000 |
| MaxOpsPerSecondPerPartition | 2000 | 1000-5000 |
| MaxGlobalOperationsPerSecond | 20000 | 10000-25000 |

---

## 📝 Document Maintenance

**Last Updated**: 2024-01-15  
**Status**: Complete for 4.8M Tranco dataset  
**Version**: 1.0

### File Inventory
| File | Lines | Focus | Audience |
|------|-------|-------|----------|
| IMPORTER_README.md | 725 | Architecture, patterns | Architects, Engineers |
| TABLE_STORAGE_OPERATIONAL_GUIDE.md | 275 | Operations, troubleshooting | DevOps, Operations |
| COST_ANALYSIS.md | 342 | Economics, ROI | Finance, Management |
| WORKER_FUNCTION_README.md | 453 | Analysis pipeline | Engineers, Architects |
| AZURE_DEPLOYMENT_GUIDE.md | 273 | Infrastructure | DevOps |
| CI-CD_README.md | 151 | Automation | DevOps |
| README.md | - | Overview | Everyone |

---

## 🔗 Cross-Reference Guide

**When you see → Look here**:

| If reading... | And need... | Then see... |
|--|--|--|
| IMPORTER_README | Operational checklists | TABLE_STORAGE_OPERATIONAL_GUIDE.md |
| IMPORTER_README | Cost impact | COST_ANALYSIS.md |
| TABLE_STORAGE_OPERATIONAL_GUIDE | Design rationale | IMPORTER_README.md § "Trade-offs" |
| COST_ANALYSIS | Technical implementation | IMPORTER_README.md § "Design Patterns" |
| Any doc | System overview | README.md |
| Any doc | Deployment | AZURE_DEPLOYMENT_GUIDE.md |
| Any doc | CI/CD | CI-CD_README.md |

---

**Esta é uma documentação viva. Feedback e sugestões são bem-vindas!**

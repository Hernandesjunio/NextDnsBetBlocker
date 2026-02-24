# NextDnsBetBlocker 🎯

## Overview

**NextDnsBetBlocker** é uma solução em tempo real para **bloqueio automático de domínios de apostas e jogos de azar**, integrada com a plataforma **NextDNS**. A solução combina análise de domínios baseada em reputação (Tranco), classificação automática de conteúdo suspeito e sincronização eficiente com a infraestrutura de DNS na nuvem.

### Objetivo Principal

Fornecer um sistema robusto e escalável para:
- **Importação de listas de domínios** conhecidos como suspeitos ou maliciosos (5M+ domínios)
- **Análise contínua de logs de DNS** para detectar padrões de acesso a domínios problemáticos
- **Sincronização em tempo real** com NextDNS para bloqueio automático
- **Enterprise-grade Throttling** para proteção de infraestrutura, incluindo:
  - **Hierarchical Token Buckets**: Controle fino de fluxo global e por partição.
  - **Backpressure de Memória**: Canais *Bounded* para evitar estouro de memória sob carga.
  - **Circuit Breaker Inteligente**: Degradação adaptativa com *Recuperação em Degraus* (Step Recovery).
  - **Burst Dinâmico**: Ajuste automático de picos baseado na saúde da partição.

---

## Arquitetura Geral

```
┌─────────────────────────────────────────────────────────────────┐
│                     NextDnsBetBlocker System                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────────────┐         ┌─────────────────────────┐   │
│  │  IMPORTER (ACI)      │         │  WORKER / FUNCTION APP  │   │
│  │  - Runs once daily   │         │  - 24/7 cloud service  │   │
│  │  - Loads data        │  ═══>   │  - Fetches logs        │   │
│  │  - Validates         │         │  - Classifies domains  │   │
│  │  - Stores in Azure   │         │  - Publishes results   │   │
│  └──────────────────────┘         └─────────────────────────┘   │
│           │                                │                     │
│           └──────────┬──────────┬──────────┘                     │
│                      ▼          ▼                                │
│            ┌──────────────────────────┐                          │
│            │  Shared Core Services    │                          │
│            │  - Table Storage Repo    │                          │
│            │  - Blob Repository       │                          │
│            │  - Distributed Lock      │                          │
│            │  - Checkpoint Store      │                          │
│            └──────────────────────────┘                          │
│                      │                                            │
│                      ▼                                            │
│            ┌──────────────────────────┐                          │
│            │   Azure Infrastructure   │                          │
│            │  - Table Storage         │                          │
│            │  - Blob Storage          │                          │
│            │  - Queue Storage         │                          │
│            │  - Application Insights  │                          │
│            └──────────────────────────┘                          │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Componentes Principais

| Componente | Tipo | Responsabilidade | Localização |
|-----------|------|------------------|-----------|
| **Importer** | .NET Console (ACI) | Importação de listas de domínios, validação e armazenamento | `src/NextDnsBetBlocker.Worker.Importer` |
| **Worker Service** | .NET Worker | Análise contínua de logs DNS, classificação de domínios | `src/NextDnsBetBlocker.Worker` |
| **Function App** | Azure Functions | Equivalente serverless do Worker Service | `src/NextDnsBetBlocker.FunctionApp` |
| **Core Library** | .NET Class Library | Serviços compartilhados, padrões, repositórios | `src/NextDnsBetBlocker.Core` |
| **Tests** | xUnit | Cobertura de testes para Core Services | `tests/NextDnsBetBlocker.Core.Tests` |

---

## Technology Stack

### Backend
- **.NET 10** (C# 14) - Framework moderno, cloud-native
- **Azure Services** - Table Storage, Blob Storage, Queue Storage, Application Insights
- **Dependency Injection** - Microsoft.Extensions.DependencyInjection
- **Logging** - ILogger com múltiplos providers

### Data Sources
- **Hagezi Lists** - Listas públicas de domínios suspeitos
- **Tranco Top 4.8M** - Cache de domínios legítimos (4.8 milhões de domínios confiáveis)
- **NextDNS** - Integração com resolver DNS para bloqueio

### Infrastructure as Code
- **Bicep** - Templates para provisionamento Azure
- **Docker** - Containerização do Importer (ACI)
- **GitHub Actions** - CI/CD pipelines

---

## Fluxos de Trabalho

### 1. **Importação de Dados** (Diário)
```
Importer (ACI) → Fetch Lists → Validate → Batch Processing → Table Storage
```
- **Frequência**: Uma vez por dia (agendado via ACI)
- **Entrada**: Listas Hagezi públicas
- **Saída**: Domínios armazenados em Table Storage com particionamento

**[Detalhes técnicos em →](docs/IMPORTER_README.md)**

### 2. **Análise em Tempo Real** (Contínuo)
```
NextDNS Logs → Worker/Function → Classify → Publish → Queue → Action
```
- **Frequência**: Contínua (Worker) ou agendada (Function App)
- **Entrada**: Logs de queries DNS do NextDNS
- **Saída**: Domínios suspeitos publicados em fila para bloqueio

**[Detalhes técnicos em →](docs/WORKER_FUNCTION_README.md)**

---

## Padrões de Design & Resilience

A solução implementa padrões modernos para garantir robustez em ambientes cloud:

### **Throttling Hierárquico**
- Rate limiter por partição (2k ops/s) + global (20k ops/s)
- Controle adaptativo de paralelismo (redução de 5% em timeout)
- Backpressure automática via Channels bounded

### **Graceful Degradation**
- Falha de uma partição não afeta outras
- Retry com backoff exponencial por partição isolada
- Continue-on-error com logging detalhado

### **Pipeline Paralelo com Channels**
- Producer/Consumer pattern para desacoplamento
- Isolated batch processing por partição
- Pipelining automático com SemaphoreSlim

### **Distributed Lock**
- Sincronização entre instâncias via Blob Storage
- Evita processamento duplicado
- Tolerância a falhas de lock

### **Checkpoint Store**
- Rastreamento de progresso entre execuções
- Recuperação eficiente de falhas
- Auditoria de importações

---

## Como Começar

### Pré-requisitos
- .NET 10 SDK
- Azure Storage Account
- Docker (para executar Importer localmente)

### Setup Local
```bash
# Clone o repositório
git clone https://github.com/seu-usuario/NextDnsBetBlocker.git
cd NextDnsBetBlocker

# Configure appsettings.json e user secrets
# Consulte docs/AZURE_DEPLOYMENT_GUIDE.md

# Execute os testes
dotnet test

# Execute o Worker localmente
dotnet run --project src/NextDnsBetBlocker.Worker
```

### Deployment
- **Importer**: Azure Container Instances (ACI) com agendamento
- **Worker**: Azure App Service / Container Apps
- **Function App**: Azure Functions Consumption Plan

Veja [CI-CD_README.md](docs/CI-CD_README.md) para detalhes de deployment.

---

## Documentação Detalhada

- **[IMPORTER_README.md](docs/IMPORTER_README.md)** - Arquitetura, padrões e otimizações do Importer
  - Pipeline sequencial, design patterns (adaptive parallelism, graceful degradation)
  - **Seção: Table Storage Optimization** com estratégias de particionamento e rate limiting
  - Performance characteristics para 4.8M+ domínios

- **[WORKER_FUNCTION_README.md](docs/WORKER_FUNCTION_README.md)** - Pipeline de análise, deployment e operação
  - Comparação Worker vs Function App
  - Padrões de resiliência (distributed lock, checkpoint recovery)

- **[TABLE_STORAGE_OPERATIONAL_GUIDE.md](docs/TABLE_STORAGE_OPERATIONAL_GUIDE.md)** - Guia prático para operadores
  - Pre-import checklist, monitoramento em tempo real
  - Troubleshooting playbook para 429 errors, timeouts, hot-spots
  - Cost monitoring e otimizações operacionais

- **[COST_ANALYSIS.md](docs/COST_ANALYSIS.md)** - Análise de custos e ROI
  - Modelo de custo detalhado (storage + transactions)
  - Cenários (standard, premium, archive) e trade-offs
  - Otimizações com ROI (batch sizing, retention, etc)
  - Projeção 3 anos

- **[AZURE_DEPLOYMENT_GUIDE.md](docs/AZURE_DEPLOYMENT_GUIDE.md)** - Setup e provisionamento da infraestrutura
- **[CI-CD_README.md](docs/CI-CD_README.md)** - Pipelines de build e deploy automatizados

---

## Contribuindo

Este é um projeto de portfólio demonstrando padrões modernos de .NET cloud-native. Sugestões e melhorias são bem-vindas!

---

## Observações Importantes

- Este projeto foi desenvolvido como um **portfólio profissional** para demonstrar:
  - Padrões de design (Pipeline, Producer-Consumer, Circuit Breaker)
  - Práticas cloud-native (.NET, Azure)
  - Otimização de performance e throughput
  - Resilience e fault tolerance
  
- Consulte a documentação específica de cada camada para entender decisões arquiteturais e trade-offs.

---

**Criado com foco em: Escalabilidade, Resiliência, Performance e Padrões Modernos de Engenharia de Software.**

# Algoritmo de Seed de Domínios Bloqueados

## Visão Geral
O sistema agora realiza um seed inicial dos domínios bloqueados contidos em `data/blocked.txt` apenas uma vez, armazenando essa informação no Table Storage para futuras execuções.

## Fluxo de Execução

### 1. **Inicialização (`Program.cs`)**
```
Iniciar aplicação
    ↓
Criar host e registrar serviços
    ↓
Seed checkpoint padrão (se não existir)
    ↓
Executar BlockedDomainsSeeder.SeedBlockedDomainsAsync()
    ↓
Iniciar WorkerService
```

### 2. **BlockedDomainsSeeder.SeedBlockedDomainsAsync()**

#### Passo 1: Verificar se o Seed Já Foi Feito
```csharp
var seedCheckpoint = await _checkpointStore.GetLastTimestampAsync("SEED_BLOCKED_DOMAINS");
if (seedCheckpoint.HasValue)
{
    // Seed já foi executado, retorna
    return;
}
```

**Por quê?** Usa um checkpoint especial na tabela `AgentState` com key `"SEED_BLOCKED_DOMAINS"`. Se existir, significa que o seed já foi feito anteriormente.

#### Passo 2: Validar Arquivo
```csharp
if (!File.Exists(blockedDomainsFilePath))
{
    // Arquivo não encontrado, pula seed
    return;
}
```

#### Passo 3: Parse dos Domínios
```csharp
var domains = ParseBlockedDomains(blockedDomainsFilePath);
```

**O que faz:**
- Lê linha por linha do arquivo
- Remove comentários (linhas que começam com `#`)
- Remove linhas vazias
- Remove prefixo `*.` dos domínios (wildcard)
- Remove duplicatas

**Exemplo:**
```
Input:  *.tigrinho.io
Output: tigrinho.io

Input:  *.vem7777.com
Output: vem7777.com
```

#### Passo 4: Marcar Domínios como Bloqueados
```csharp
foreach (var domain in domains)
{
    var isAlreadyBlocked = await _blockedDomainStore.IsBlockedAsync(profileId, domain);
    if (!isAlreadyBlocked)
    {
        await _blockedDomainStore.MarkBlockedAsync(profileId, domain);
        successCount++;
    }
    else
    {
        skipCount++;
    }
}
```

**Idempotência:** Verifica se o domínio já está marcado como bloqueado antes de adicionar novamente.

#### Passo 5: Marcar Seed como Concluído
```csharp
await _checkpointStore.UpdateLastTimestampAsync("SEED_BLOCKED_DOMAINS", DateTime.UtcNow);
```

**Por quê?** Previne que o seed seja executado novamente nas próximas inicializações.

---

## Dados Armazenados

### Tabela: `AgentState`
```
PartitionKey: "checkpoint"
RowKey: "SEED_BLOCKED_DOMAINS"
Timestamp: <data/hora do seed>
LastTimestamp: <data/hora do seed>
```

### Tabela: `BlockedDomains`
```
PartitionKey: "71cb47" (ProfileId)
RowKey: "tigrinho.io" (Domain)
BlockedAt: <data/hora do seed>
```

---

## Idempotência e Segurança

✅ **Idempotente:** Pode ser executado múltiplas vezes sem duplicar dados
- Usa checkpoint para verificar se já foi feito
- Valida se domínio já existe antes de adicionar

✅ **Tolerante a falhas:**
- Se arquivo não existir, pula silenciosamente
- Cada domínio é processado individualmente com tratamento de erro
- Logs detalhados de sucesso e falhas

✅ **Sem impacto em execuções futuras:**
- Uma vez marcado como concluído, não será executado novamente
- Não interfere com o processamento normal de logs

---

## Logging

Exemplo de saída esperada:

```
Starting seed of blocked domains from C:\...\data\blocked.txt
Parsed 250 domains from blocked domains file
Marked domain tigrinho.io as blocked in profile 71cb47
Marked domain vem7777.com as blocked in profile 71cb47
...
Blocked domains seed completed: 250 domains added, 0 already blocked
```

---

## Arquivo: blocked.txt

Formato esperado:
```
# Domínios bloqueados
*.tigrinho.io
*.vem7777.com

# Comentários são ignorados
*.ser777.com
*.bis777.win
```

O sistema:
1. Remove `#` comentários
2. Remove linhas vazias
3. Remove `*.` de cada domínio
4. Remove duplicatas
5. Armazena apenas o domínio base

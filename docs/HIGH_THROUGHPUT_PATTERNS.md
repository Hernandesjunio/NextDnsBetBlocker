# High-Throughput & Resiliency Patterns 🚀

Este documento consolida os padrões arquiteturais de código utilizados no **NextDnsBetBlocker** para atingir alta vazão (20k+ ops/s) com estabilidade no Azure. Use este guia como referência para implementar sistemas similares em outros projetos.

---

## 1. Backpressure (Contrapressão) de Memória

Evita erros `OutOfMemoryException` quando a produção de dados (leitura/download) é mais rápida que o consumo (escrita/banco).

**Problema**: Em sistemas C# tradicionais (`List<T>`), o produtor enche a memória antes que o consumidor consiga esvaziar.
**Solução**: Usar `System.Threading.Channels` com `BoundedChannelOptions` e modo `Wait`.

### Pattern Implementation

```csharp
// Configuração do Canal (Buffer)
var channelOptions = new BoundedChannelOptions(capacity: 500)
{
    FullMode = BoundedChannelFullMode.Wait, // ⚠️ CRÍTICO: Bloqueia o produtor se cheio
    SingleReader = false, // Múltiplos consumers (workers)
    SingleWriter = true   // Único producer
};
var channel = Channel.CreateBounded<T>(channelOptions);

// Producer (Bloqueia se canal cheio)
await channel.Writer.WriteAsync(item);

// Consumer
await foreach (var item in channel.Reader.ReadAllAsync())
{
    // Process item
}
```

---

## 2. Token Bucket Throttling (Hierárquico)

Limita a taxa de requisições para proteger APIs externas ou bancos de dados (ex: Azure Table Storage Limits).

**Requisito**: Permitir picos curtos (burst) mas manter média estável.
**Implementação**: Algoritmo Token Bucket.

### Core Logic (TokenBucket)

```csharp
public class TokenBucket
{
    private readonly double _capacity; // Burst items
    private readonly double _tokensPerMs; // Rate items/ms
    private double _availableTokens;
    private long _lastRefillTimestamp;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task ConsumeAsync(int tokensToConsume)
    {
        while (true)
        {
            await _lock.WaitAsync();
            try
            {
                Refill(); // 1. Recalcula tokens baseados no tempo passado
                if (_availableTokens >= tokensToConsume)
                {
                    _availableTokens -= tokensToConsume; // 2. Consome
                    return;
                }
                // 3. Calcula espera necessária
                double missing = tokensToConsume - _availableTokens;
                waitTimeMs = (int)Math.Ceiling(missing / _tokensPerMs);
            }
            finally { _lock.Release(); }
            
            await Task.Delay(waitTimeMs); // 4. Espera sem travar thread
        }
    }
}
```

---

## 3. Adaptive Circuit Breaker com Step Recovery

Para sistemas que sofrem degradação de conectividade ou limites de API (Error 429).

**Diferencial**: Em vez de "tudo ou nada" (Aberto/Fechado), usa recuperação gradual.

### Estado de Degradação

```csharp
public class PartitionDegradationState
{
    // Recuperação em Degraus (Step Recovery)
    public bool RecoverGradually(int originalLimit)
    {
        // 1. Evita recuperação muito frequente (flapping)
        if ((DateTime.UtcNow - _lastRecoveryTime).TotalSeconds < 5) return false;

        if (CurrentDegradedLimit < originalLimit)
        {
            // 2. Sobe apenas 10% por vez
            int step = Math.Max(1, (int)(originalLimit * 0.1));
            CurrentDegradedLimit = Math.Min(originalLimit, CurrentDegradedLimit + step);
            
            _lastRecoveryTime = DateTime.UtcNow;
            return true;
        }
        return false;
    }
}
```

---

## 4. Burst Synchronization Fix

Crítico para sistemas onde o limite ("limit") muda dinamicamente (degradação). O "burst" deve acompanhar o novo limite para evitar picos inválidos.

### Lógica de Sincronização

```csharp
// Dentro do método ExecuteAsync do Throttler
int effectiveLimit = GetEffectivePartitionLimit(pk);
int burst = Math.Max(1, (int)(effectiveLimit * 0.1)); // 10% do limite ATUAL

// Se o limite mudou (degradação), o bucket DEVE ser recriado/ajustado
if (partitionBucket.Rate != effectiveLimit)
{
    // Reinstancia bucket com novo rate e novo burst
    _partitionBuckets[partitionKey] = new TokenBucket(effectiveLimit, burst);
}
```

---

## 5. Resiliência com Polly (Retry + Jitter)

Evita "Thundering Herd Problem" (todos tentando reconectar ao mesmo tempo) usando Jitter (aleatoriedade).

### Policy Definition

```csharp
var retryPolicy = Policy
    .HandleResult<BatchOperationResult>(r => !r.IsSuccess)
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: (retryAttempt) => 
        {
            // Exponencial: 2, 4, 8s...
            var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
            // Jitter: +/- 0-500ms aleatórios
            var jitter = TimeSpan.FromMilliseconds(new Random().Next(0, 500));
            return baseDelay + jitter;
        }
    );
```

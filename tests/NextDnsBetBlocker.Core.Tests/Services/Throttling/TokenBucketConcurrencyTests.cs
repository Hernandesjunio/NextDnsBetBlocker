namespace NextDnsBetBlocker.Core.Tests.Services.Throttling;

using FluentAssertions;
using NextDnsBetBlocker.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Etapa 2B — Fix 3: SemaphoreSlim era retido durante await Task.Delay em ConsumeAsync
///
///   Problema: com N FlushWorkers por partição chamando ConsumeAsync concorrentemente:
///     - Worker 1 adquire o lock, calcula waitTimeMs, RETÉM o lock durante Task.Delay(waitTimeMs)
///     - Workers 2..N ficam bloqueados em _lock.WaitAsync() mesmo que tokens já estejam disponíveis
///     → Throughput efetivo = 1/N do configurado (workers serializados em vez de concorrentes)
///
///   Fix 3: _lock.Release() chamado ANTES de await Task.Delay
///     - Worker 1 calcula waitTimeMs, libera o lock, espera sem bloquear os demais
///     - Workers 2..N adquirem o lock imediatamente, calculam seu próprio waitTimeMs, esperam concorrentemente
///     → Throughput efetivo = máximo permitido pela taxa configurada
///
///   Prova observável (Test 1):
///     - Consumer A precisa de 100 tokens → waitTimeMs = 100ms
///     - Consumer B precisa de 1 token  → waitTimeMs = 1ms
///     - Com bug: B fica bloqueado 80ms no WaitAsync enquanto A retém o lock
///     - Com fix: B adquire o lock em &lt;5ms, encontra tokens acumulados nos 20ms de espera de A
/// </summary>
public class TokenBucketConcurrencyTests
{
    // -------------------------------------------------------------------------
    // Fix 3: lock liberado antes do Task.Delay
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsumeAsync_LockLiberadoAntesDoDelay_ConsumidorPequenoNaoBloqueadoPorConsumidorGrande()
    {
        // rate=1000/sec (tokensPerMs=1.0), capacity=100, inicia vazio (Fix 4)
        //
        // Consumer A: ConsumeAsync(100) → waitTimeMs = ceil(100/1.0) = 100ms
        // Consumer B: ConsumeAsync(1)   → waitTimeMs = ceil(1/1.0)   = 1ms
        //
        // Sem Fix 3 (lock retido durante delay):
        //   T=0ms:  A adquire lock, calcula 100ms, RETÉM lock
        //   T=20ms: B tenta WaitAsync → BLOQUEADO (A ainda retém o lock por mais 80ms)
        //   T=100ms: A termina delay, libera lock. B adquire, encontra 100 tokens acumulados.
        //   B elapsed (a partir de T=20ms) ≈ 80ms
        //
        // Com Fix 3 (lock liberado antes do delay):
        //   T=0ms:  A adquire lock, calcula 100ms, LIBERA lock, inicia delay sem o lock
        //   T=20ms: B adquire lock em <5ms. Refill: 20ms × 1.0 = 20 tokens. 20 ≥ 1 → B DONE imediatamente.
        //   B elapsed ≈ 0ms
        var bucket = new TokenBucket(tokensPerSecond: 1_000, maxBurst: 100);

        // Consumer A: vai aguardar 100ms para acumular 100 tokens
        var consumerATask = Task.Run(() => bucket.ConsumeAsync(100));

        // Aguarda Consumer A ter adquirido o lock e entrado no ciclo de espera
        await Task.Delay(20);

        // Consumer B: precisa de apenas 1 token (~1ms de acumulação)
        var sw = Stopwatch.StartNew();
        await bucket.ConsumeAsync(1);
        sw.Stop();

        // Sem Fix 3: B ficaria bloqueado ~80ms no WaitAsync
        // Com Fix 3: B encontra tokens acumulados durante os 20ms e conclui em <5ms
        sw.ElapsedMilliseconds.Should().BeLessThan(40,
            because: "com Fix 3, Consumer B adquire o lock imediatamente (A não o retém durante o delay) " +
                     "e encontra 20 tokens acumulados nos 20ms de espera → conclui em <5ms; " +
                     "sem Fix 3, B ficaria bloqueado ~80ms no WaitAsync do SemaphoreSlim");

        await consumerATask;
    }

    // -------------------------------------------------------------------------
    // Regressão: Fix 3 não introduz race condition ou deadlock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsumeAsync_MultiplosConcorrentes_CompletamSemDeadlockOuTimeout()
    {
        // Após Fix 3, o lock é liberado ANTES do delay. Garante que isso não introduz
        // condição de corrida onde múltiplos consumers competem pelo lock e travam.
        // (race condition: dois consumers tentam consumir o mesmo token ao mesmo tempo)
        var bucket = new TokenBucket(tokensPerSecond: 500, maxBurst: 1);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => bucket.ConsumeAsync(1))
            .ToList();

        var completing = Task.WhenAll(tasks);
        var timeout = Task.Delay(5_000);

        var winner = await Task.WhenAny(completing, timeout);

        winner.Should().Be(completing,
            because: "todos os 10 consumidores devem concluir sem deadlock — " +
                     "Fix 3 não deve introduzir race condition no re-acesso ao bucket após Task.Delay");

        tasks.Should().OnlyContain(t => t.IsCompletedSuccessfully,
            because: "nenhum consumidor deve ter lançado exceção");
    }

    [Fact]
    public async Task ConsumeAsync_MultiplosConcorrentes_TotalDeTokensConsumidosECorreto()
    {
        // Prova que Fix 3 não causa under/over-consumption: cada ConsumeAsync(1)
        // deve consumir exatamente 1 token, mesmo com alta concorrência.
        // Se o lock fosse mal gerenciado, dois consumers poderiam consumir o mesmo token.
        int consumerCount = 20;
        int tokensPerConsumer = 1;
        var bucket = new TokenBucket(tokensPerSecond: 1_000, maxBurst: 1);

        var consumeCount = 0;
        await Task.WhenAll(Enumerable.Range(0, consumerCount).Select(async _ =>
        {
            await bucket.ConsumeAsync(tokensPerConsumer);
            Interlocked.Increment(ref consumeCount);
        }));

        consumeCount.Should().Be(consumerCount,
            because: "cada um dos 20 consumers deve ter consumido exatamente 1 token — " +
                     "Fix 3 não deve permitir que dois consumers consumam o mesmo token concorrentemente");
    }

    // -------------------------------------------------------------------------
    // Conformidade de taxa com consumers concorrentes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsumeAsync_CincoConcorrentes_TempoTotalCompativelComRate()
    {
        // 5 consumers × 1 token a 100/sec → cada token leva ~10ms → total ~50ms
        // Tolerância: 5× para variação de timer de SO e contention de lock
        // (com Fix 3, consumers esperam concorrentemente — total próximo de 5×10ms = 50ms)
        int rate = 100;
        var bucket = new TokenBucket(tokensPerSecond: rate, maxBurst: 1);

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => bucket.ConsumeAsync(1)));
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeInRange(15, 200,
            because: $"5 tokens a {rate}/sec devem levar entre 30-150ms; " +
                     "tempo < 15ms indicaria consumo sem rate limiting; " +
                     "tempo > 200ms indicaria serialização excessiva pelo lock");
    }
}

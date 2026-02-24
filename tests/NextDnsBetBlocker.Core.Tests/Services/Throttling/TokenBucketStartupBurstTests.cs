namespace NextDnsBetBlocker.Core.Tests.Services.Throttling;

using FluentAssertions;
using NextDnsBetBlocker.Core;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Etapa 2A — Fix 4: TokenBucket iniciava com _availableTokens = Capacity (burst disponível de imediato)
///
///   Problema: ao criar o bucket, Rate+Capacity tokens podiam ser consumidos no primeiro segundo:
///     - Burst (Capacity) tokens consumidos imediatamente (startup)
///     - Rate tokens gerados pelo refill no decorrer do segundo
///     → Total possível no 1º segundo = Rate + Capacity, excedendo o limite da Azure (~2000 ops/s)
///
///   Fix 4: _availableTokens = 0 — bucket inicia vazio, tokens acumulam pelo refill normal.
///     → Máximo no 1º segundo = Rate (sem spike inicial)
///
///   Exemplo real: Rate=2000/s, Capacity=200 (10% burst)
///     Sem Fix 4: até 2200 ops no primeiro segundo → Azure retorna 429
///     Com Fix 4: até 2000 ops no primeiro segundo → dentro do limite
/// </summary>
public class TokenBucketStartupBurstTests
{
    // -------------------------------------------------------------------------
    // Fix 4: _availableTokens = 0 (sem burst de startup)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TokenBucket_NovoBucket_PrimeiroConsumo_NaoRetornaImediato()
    {
        // Sem Fix 4: _availableTokens = Capacity = 20 → ConsumeAsync(1) retorna imediatamente (~0ms)
        // Com Fix 4: _availableTokens = 0 → aguarda acumulação de 1 token a 100/sec = ~10ms
        var bucket = new TokenBucket(tokensPerSecond: 100, maxBurst: 20);

        var sw = Stopwatch.StartNew();
        await bucket.ConsumeAsync(1);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(5,
            because: "novo bucket deve iniciar sem tokens — sem Fix 4 retornaria imediato (~0ms) " +
                     "consumindo o burst de 20 tokens antes do refill, inflando a taxa do 1º segundo");
    }

    [Fact]
    public async Task TokenBucket_NovoBucket_BurstInteiro_NaoDisponivelDeImediato()
    {
        // Sem Fix 4: 10 tokens de burst disponíveis → 10 consumes imediatos (~0ms total)
        // Com Fix 4: 10 tokens precisam acumular a 50/sec (1 token/20ms) → ~200ms total
        // Distinção clara: 0ms (bug) vs 200ms (fix) → assert > 100ms
        var bucket = new TokenBucket(tokensPerSecond: 50, maxBurst: 10);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
            await bucket.ConsumeAsync(1);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThan(100,
            because: "sem burst inicial, consumir 10 tokens a 50/sec deve levar ~200ms — " +
                     "sem Fix 4 seriam imediatos, causando pico acima do limite configurado");
    }

    [Fact]
    public async Task TokenBucket_ConformidadeAzureTableStorage_BurstDeStartupNaoExcedeLimite()
    {
        // Cenário real: Azure Table Storage = 2000 ops/sec por partição, burst = 200 (10%)
        //
        // Sem Fix 4: _availableTokens = 200 → ConsumeAsync(200) retorna imediatamente (~0ms)
        //            → 200 tokens extras no 1º segundo = 2200 total → Azure retorna 429
        //
        // Com Fix 4: _availableTokens = 0 → ConsumeAsync(200) aguarda:
        //            missing = 200, tokensPerMs = 2.0, waitTimeMs = ceil(200/2.0) = 100ms
        //            → sem pico de startup, 1º segundo fica dentro de 2000 ops
        var bucket = new TokenBucket(tokensPerSecond: 2_000, maxBurst: 200);

        var sw = Stopwatch.StartNew();
        await bucket.ConsumeAsync(200); // lote típico = tamanho do burst
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(50,
            because: "consumir 200 tokens (= burst) a 2000/sec deve levar ~100ms — " +
                     "sem Fix 4 seria imediato, gerando 200 ops extras no startup que excederiam o limite da Azure");
    }

    [Fact]
    public async Task TokenBucket_PrimeiroSegundo_TaxaTotal_NaoExcedeRateMaisCapacity()
    {
        // Prova quantitativa: sem Fix 4, o 1º segundo poderia consumir Rate+Capacity tokens.
        // Com Fix 4, o máximo é apenas Rate tokens.
        //
        // rate=200/sec, burst=40 (20%), janela=500ms
        //   Sem Fix 4: 40 (burst) + 100 (rate×0.5) = 140 tokens
        //   Com Fix 4:  0 (sem burst) + 100 (rate×0.5) = ~100 tokens
        //
        // Threshold: 120 tokens → separa os cenários (100 ≤ 120 < 140)
        int rate = 200;
        int burst = 40;
        var bucket = new TokenBucket(tokensPerSecond: rate, maxBurst: burst);

        int consumed = 0;
        var deadline = Stopwatch.StartNew();

        while (deadline.ElapsedMilliseconds < 500)
        {
            await bucket.ConsumeAsync(1);
            consumed++;
        }

        // Tolerância de 20% sobre a taxa teórica (rate × 0.5 = 100 tokens)
        int maxExpected = (int)(rate * 0.5 * 1.2); // 120

        consumed.Should().BeLessThanOrEqualTo(maxExpected,
            because: $"em 500ms a taxa máxima é {rate * 0.5:F0} tokens; " +
                     $"sem Fix 4 seriam ~{rate * 0.5 + burst} ({burst} burst + taxa), excedendo o limite");
    }
}

namespace NextDnsBetBlocker.Core.Tests.Services.Throttling;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NextDnsBetBlocker.Core;
using System;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Etapa 1 — Correções críticas em PartitionDegradationState:
///
///   Fix 1: CurrentDegradedLimit iniciava em 0
///           → primeiro erro calculava 0 - step = negativo → clampa direto ao mínimo
///           → comportamento correto: primeiro erro deve reduzir de originalLimit
///
///   Fix 2: ResetCircuitBreaker() setava CurrentDegradedLimit = 0
///           → GetEffectivePartitionLimit retornava 0
///           → TokenBucket criado com rate=0 → _tokensPerMs=0
///           → ConsumeAsync: Task.Delay((int)Infinity) = Task.Delay(int.MinValue) → crash
/// </summary>
public class PartitionDegradationStateTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AdaptiveDegradationConfig MakeConfig(
        int degradationPct = 10,
        int minPct = 50) =>
        new AdaptiveDegradationConfig(
            Enabled: true,
            DegradationPercentagePerError: degradationPct,
            MinimumDegradationPercentage: minPct,
            RecoveryIntervalSeconds: 60,
            CircuitBreakerResetIntervalSeconds: 300);

    // -------------------------------------------------------------------------
    // Fix 1: CurrentDegradedLimit inicia em 0
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordError_PrimeiroErro_DegradeDeOriginalLimit_NaoDeZero()
    {
        // Sem Fix 1: newLimit = 0 - 100 = -100 → Max(500,-100) = 500 (salta ao mínimo)
        // Com Fix 1: inicializa em 1000, newLimit = 1000 - 100 = 900 → Max(500,900) = 900
        var state = new PartitionDegradationState();
        var config = MakeConfig(degradationPct: 10, minPct: 50);

        state.RecordError(originalLimit: 1000, config);

        state.CurrentDegradedLimit.Should().Be(900,
            because: "o primeiro erro deve reduzir 10% de 1000 (→ 900), não saltar direto para o mínimo 500");
    }

    [Fact]
    public void RecordError_PrimeiroErro_NaoSaltaParaMinimo()
    {
        // Garante que o comportamento errado (salto para mínimo) não ocorre
        var state = new PartitionDegradationState();
        var config = MakeConfig(degradationPct: 10, minPct: 50);

        state.RecordError(originalLimit: 1000, config);

        state.CurrentDegradedLimit.Should().NotBe(500,
            because: "sem Fix 1 o limite saltaria incorretamente para o mínimo 500 no primeiro erro");
    }

    [Fact]
    public void RecordError_DoisErros_DegradeProgressivamentePorStep()
    {
        // Prova que a progressão é 1000 → 900 → 800, não 0 → 500 → 500
        var state = new PartitionDegradationState();
        var config = MakeConfig(degradationPct: 10, minPct: 50);

        state.RecordError(originalLimit: 1000, config); // 1000 → 900
        state.RecordError(originalLimit: 1000, config); // 900  → 800

        state.CurrentDegradedLimit.Should().Be(800);
    }

    [Fact]
    public void RecordError_QuandoAtingeMinimo_ClampaNaMinimo()
    {
        // Config: 40% por erro, mínimo 50% → minLimit = 500
        // Erro 1: 1000 → 600
        // Erro 2: 600 - 400 = 200 → clampado em 500
        var state = new PartitionDegradationState();
        var config = MakeConfig(degradationPct: 40, minPct: 50);

        state.RecordError(originalLimit: 1000, config);
        state.RecordError(originalLimit: 1000, config);

        state.CurrentDegradedLimit.Should().Be(500,
            because: "o limite nunca deve ficar abaixo do mínimo configurado (50% de 1000 = 500)");
    }

    // -------------------------------------------------------------------------
    // Fix 2: ResetCircuitBreaker setava CurrentDegradedLimit = 0
    // -------------------------------------------------------------------------

    [Fact]
    public void ResetCircuitBreaker_SetaLimiteNoMinimoPorcentagem_NaoZero()
    {
        // Sem Fix 2: CurrentDegradedLimit = 0  →  rate=0  →  crash
        // Com Fix 2: CurrentDegradedLimit = Max(1, 1000 * 80/100) = 800
        var state = new PartitionDegradationState();
        state.OpenCircuitBreaker();

        state.ResetCircuitBreaker(originalLimit: 1000, minimumDegradationPercentage: 80);

        state.CurrentDegradedLimit.Should().Be(800,
            because: "o reset deve restaurar o limite ao mínimo permitido (80% de 1000), não a 0");
    }

    [Fact]
    public void ResetCircuitBreaker_CurrentDegradedLimit_SemprePositivo()
    {
        // Prova a causa-raiz do crash: limit=0 → _tokensPerMs=0 → Task.Delay(Infinity)
        var state = new PartitionDegradationState();
        state.OpenCircuitBreaker();

        state.ResetCircuitBreaker(originalLimit: 1000, minimumDegradationPercentage: 80);

        state.CurrentDegradedLimit.Should().BeGreaterThan(0,
            because: "limite=0 cria TokenBucket com _tokensPerMs=0, resultando em Task.Delay(int.MinValue) → crash");
    }

    [Fact]
    public void ResetCircuitBreaker_TransicionaParaEstadoDegraded_NaoOpen()
    {
        var state = new PartitionDegradationState();
        state.OpenCircuitBreaker();
        state.CircuitBreakerState.Should().Be(CircuitBreakerState.Open);

        state.ResetCircuitBreaker(originalLimit: 1000, minimumDegradationPercentage: 80);

        state.CircuitBreakerState.Should().Be(CircuitBreakerState.Degraded,
            because: "o reset representa half-open (Degraded), não fechamento total");
    }

    [Fact]
    public void ResetCircuitBreaker_LimiteResultante_PermiteCriarTokenBucketFuncional()
    {
        // Prova direta que o TokenBucket(rate=0) não ocorre após o reset
        // Sem Fix 2: limit=0 → burst=Max(1,0)=1 → Rate=0 → _tokensPerMs=0
        // Com Fix 2: limit=800 → burst=80 → Rate=800 → _tokensPerMs=0.8 → funciona
        var state = new PartitionDegradationState();
        state.OpenCircuitBreaker();

        state.ResetCircuitBreaker(originalLimit: 1000, minimumDegradationPercentage: 80);

        int limit = state.CurrentDegradedLimit;
        int burst = Math.Max(1, (int)(limit * 0.1));
        var bucket = new TokenBucket(limit, burst);

        bucket.Rate.Should().BeGreaterThan(0,
            because: "TokenBucket.Rate=0 causa _tokensPerMs=0, resultando em Task.Delay(Infinity)");
    }

    // -------------------------------------------------------------------------
    // Integração: HierarchicalThrottler não trava após reset do circuit breaker
    // -------------------------------------------------------------------------

    /// <summary>
    /// Prova que ExecuteAsync completa após o ciclo completo de abertura → reset do circuit breaker,
    /// sem lançar ArgumentOutOfRangeException (que ocorria quando TokenBucket era criado com rate=0).
    ///
    /// Config: partitionLimit=1000, 10% por erro, min=80% → minLimit=800
    ///   Erro 1: 1000 → 900 (circuit Closed)
    ///   Erro 2: 900  → 800 = minLimit → circuit Open
    ///   [aguarda 1.2s]
    ///   Erro 3: aciona reset → CurrentDegradedLimit=800, estado=Degraded → circuit Open novamente
    ///   ExecuteAsync: GetEffectivePartitionLimit → Max(1,800)=800 → TokenBucket(800,80) → sem crash
    /// </summary>
    [Fact]
    public async Task HierarchicalThrottler_AposResetCircuitBreaker_ExecuteAsyncNaoLancaExcecao()
    {
        var config = new AdaptiveDegradationConfig(
            Enabled: true,
            DegradationPercentagePerError: 10,
            MinimumDegradationPercentage: 80,
            RecoveryIntervalSeconds: 1,
            CircuitBreakerResetIntervalSeconds: 1);

        var mockLogger = new Mock<ILogger<HierarchicalThrottler>>();
        var throttler = new HierarchicalThrottler(
            globalLimitPerSecond: 100_000,
            partitionLimitPerSecond: 1_000,
            logger: mockLogger.Object,
            degradationConfig: config);

        // Forçar abertura com 2 erros: 1000 → 900 → 800 = minLimit → Open
        throttler.RecordError("pk-integracao", new Exception("erro 1"));
        throttler.RecordError("pk-integracao", new Exception("erro 2"));

        throttler.GetCircuitBreakerState("pk-integracao")
            .Should().Be(CircuitBreakerState.Open);

        // Aguardar o intervalo de reset configurado (1s + margem)
        await Task.Delay(millisecondsDelay: 1_200);

        // Acionar o caminho de reset — mesmo fluxo que ocorre em produção
        throttler.RecordError("pk-integracao", new Exception("erro após cooldown"));

        // Act: ExecuteAsync deve completar sem lançar exceção
        // Sem Fix 2: GetEffectivePartitionLimit retornava 0 → TokenBucket(0,1)
        //            → 2ª tentativa de ConsumeAsync → Task.Delay(int.MinValue) → crash
        var executed = false;
        var act = async () =>
        {
            await throttler.ExecuteAsync("pk-integracao", recordCount: 1, async () =>
            {
                executed = true;
                await Task.CompletedTask;
            });
        };

        await act.Should().NotThrowAsync(
            because: "GetEffectivePartitionLimit não deve retornar 0 após reset do circuit breaker");

        executed.Should().BeTrue(
            because: "a callback deve ter sido invocada com sucesso");
    }
}

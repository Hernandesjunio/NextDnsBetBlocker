namespace NextDnsBetBlocker.Core.Tests.Services.Throttling;

using Microsoft.Extensions.Logging;
using Moq;
using NextDnsBetBlocker.Core;
using System;
using Xunit;

/// <summary>
/// Testes para verificar que HierarchicalThrottler usa ILogger em vez de Console.WriteLine
/// </summary>
public class HierarchicalThrottlerLoggingTests
{
    [Fact]
    public void RecordError_LogsCircuitBreakerReset_WhenCircuitBreakerIsOpen()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<HierarchicalThrottler>>();
        var metrics = new ShardingProcessorMetrics();
        var degradationConfig = new AdaptiveDegradationConfig(
            Enabled: true,
            DegradationPercentagePerError: 10,
            MinimumDegradationPercentage: 80,
            RecoveryIntervalSeconds: 1,
            CircuitBreakerResetIntervalSeconds: 1);

        var throttler = new HierarchicalThrottler(
            globalLimitPerSecond: 1000,
            partitionLimitPerSecond: 100,
            degradationConfig: degradationConfig,
            metrics: metrics,
            logger: mockLogger.Object);

        // Simular erros para abrir circuit breaker
        for (int i = 0; i < 3; i++)
        {
            throttler.RecordError("partition-1", new Exception("Test error"));
        }

        // Esperar intervalo de reset
        System.Threading.Thread.Sleep(1500);

        // Act
        throttler.RecordError("partition-1", new Exception("Test error"));

        // Assert - Verificar que LogInformation foi chamado para circuit breaker reset
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Circuit breaker reset")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void RecordError_LogsDegradation_WhenLimitChanges()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<HierarchicalThrottler>>();
        var metrics = new ShardingProcessorMetrics();
        var degradationConfig = new AdaptiveDegradationConfig(
            Enabled: true,
            DegradationPercentagePerError: 10,
            MinimumDegradationPercentage: 50,
            RecoveryIntervalSeconds: 60,
            CircuitBreakerResetIntervalSeconds: 300);

        var throttler = new HierarchicalThrottler(
            globalLimitPerSecond: 1000,
            partitionLimitPerSecond: 100,
            degradationConfig: degradationConfig,
            metrics: metrics,
            logger: mockLogger.Object);

        // Act - Registrar erro para degradar
        throttler.RecordError("partition-1", new Exception("Test error"));

        // Assert - Verificar que LogWarning foi chamado para degradação
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Partition")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void RecordError_LogsCritical_WhenCircuitBreakerOpens()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<HierarchicalThrottler>>();
        var metrics = new ShardingProcessorMetrics();
        var degradationConfig = new AdaptiveDegradationConfig(
            Enabled: true,
            DegradationPercentagePerError: 10,
            MinimumDegradationPercentage: 80,
            RecoveryIntervalSeconds: 60,
            CircuitBreakerResetIntervalSeconds: 300);

        var throttler = new HierarchicalThrottler(
            globalLimitPerSecond: 1000,
            partitionLimitPerSecond: 100,
            degradationConfig: degradationConfig,
            metrics: metrics,
            logger: mockLogger.Object);

        // Act - Registrar múltiplos erros para abrir circuit breaker
        for (int i = 0; i < 5; i++)
        {
            throttler.RecordError("partition-1", new Exception("Test error"));
        }

        // Assert - Verificar que LogCritical foi chamado para circuit breaker opened
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Circuit breaker opened")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void RecordSuccess_LogsRecovery_WhenPartitionRecovering()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<HierarchicalThrottler>>();
        var metrics = new ShardingProcessorMetrics();
        var degradationConfig = new AdaptiveDegradationConfig(
            Enabled: true,
            DegradationPercentagePerError: 10,
            MinimumDegradationPercentage: 50,
            RecoveryIntervalSeconds: 1,
            CircuitBreakerResetIntervalSeconds: 300);

        var throttler = new HierarchicalThrottler(
            globalLimitPerSecond: 1000,
            partitionLimitPerSecond: 100,
            degradationConfig: degradationConfig,
            metrics: metrics,
            logger: mockLogger.Object);

        // Registrar erro para degradar
        throttler.RecordError("partition-1", new Exception("Test error"));

        // Esperar intervalo de recuperação
        System.Threading.Thread.Sleep(1500);

        // Act - Registrar sucesso para recuperar
        throttler.RecordSuccess("partition-1");

        // Assert - Verificar que LogInformation foi chamado para recuperação
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("recovering")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void RecordError_DoesNotLogAnything_WhenDegradationDisabled()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<HierarchicalThrottler>>();
        var metrics = new ShardingProcessorMetrics();
        var degradationConfig = new AdaptiveDegradationConfig(Enabled: false);

        var throttler = new HierarchicalThrottler(
            globalLimitPerSecond: 1000,
            partitionLimitPerSecond: 100,
            degradationConfig: degradationConfig,
            metrics: metrics,
            logger: mockLogger.Object);

        // Act
        throttler.RecordError("partition-1", new Exception("Test error"));

        // Assert - Verificar que nenhum log foi feito
        mockLogger.Verify(x => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    [Fact]
    public void Constructor_AcceptsNullLogger()
    {
        // Arrange & Act
        var throttler = new HierarchicalThrottler(
            globalLimitPerSecond: 1000,
            partitionLimitPerSecond: 100,
            degradationConfig: null,
            metrics: null,
            logger: null);

        // Assert
        Assert.NotNull(throttler);
    }
}

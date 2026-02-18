using FluentAssertions;
using Moq;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services.Import;
using Xunit;
using Microsoft.Extensions.Logging;

namespace NextDnsBetBlocker.Core.Tests.Services.Import;

public class ListImportOrchestratorTests
{
    private readonly Mock<ILogger<ListImportOrchestrator>> _mockLogger;
    private readonly Mock<IListTableStorageRepository> _mockTableRepository;
    private readonly Mock<IImportMetricsCollector> _mockMetricsCollector;
    private readonly Mock<IImportRateLimiter> _mockRateLimiter;
    private readonly Mock<IPartitionKeyStrategy> _mockPartitionKeyStrategy;
    private readonly Mock<IProgressReporter> _mockProgressReporter;
    private readonly ParallelImportConfig _parallelConfig;
    private readonly ListImportOrchestrator _orchestrator;

    public ListImportOrchestratorTests()
    {
        _mockLogger = new Mock<ILogger<ListImportOrchestrator>>();
        _mockTableRepository = new Mock<IListTableStorageRepository>();
        _mockMetricsCollector = new Mock<IImportMetricsCollector>();
        _mockRateLimiter = new Mock<IImportRateLimiter>();
        _mockPartitionKeyStrategy = new Mock<IPartitionKeyStrategy>();
        _mockProgressReporter = new Mock<IProgressReporter>();

        _parallelConfig = new ParallelImportConfig
        {
            MaxDegreeOfParallelism = 4
        };

        _mockPartitionKeyStrategy
            .Setup(p => p.GetPartitionKey(It.IsAny<string>()))
            .Returns((string domain) => $"partition-{domain[0]}");

        _mockTableRepository
            .Setup(t => t.EnsureTableExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRateLimiter
            .Setup(r => r.WaitAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var currentMetrics = new ImportMetrics
        {
            Status = ImportStatus.Running,
            TotalProcessed = 0,
            TotalInserted = 0,
            TotalErrors = 0
        };

        _mockMetricsCollector
            .Setup(m => m.GetCurrentMetrics())
            .Returns(currentMetrics);

        _orchestrator = new ListImportOrchestrator(
            _mockLogger.Object,
            _mockTableRepository.Object,
            _mockMetricsCollector.Object,
            _mockRateLimiter.Object,
            _mockPartitionKeyStrategy.Object,
            _mockProgressReporter.Object,
            _parallelConfig);
    }

    [Fact]
    public async Task ExecuteImportAsync_WithAddOperation_CallsUpsertBatch()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container",
            BatchSize = 100
        };

        var domains = new[] { "example.com", "test.com", "sample.org" };

        var batchResult = new BatchOperationResult
        {
            BatchId = "batch-1",
            ItemCount = 3,
            SuccessCount = 3,
            FailureCount = 0
        };

        _mockTableRepository
            .Setup(t => t.UpsertBatchAsync(
                config.TableName,
                It.IsAny<List<DomainListEntry>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Act
        var result = await _orchestrator.ExecuteImportAsync(
            config,
            ImportOperationType.Add,
            domains,
            progress,
            cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(ImportStatus.Completed);

        _mockTableRepository.Verify(
            t => t.UpsertBatchAsync(
                config.TableName,
                It.IsAny<List<DomainListEntry>>(),
                cts.Token),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteImportAsync_WithRemoveOperation_CallsDeleteBatch()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container",
            BatchSize = 100
        };

        var domains = new[] { "example.com", "test.com" };

        var batchResult = new BatchOperationResult
        {
            BatchId = "batch-1",
            ItemCount = 2,
            SuccessCount = 2,
            FailureCount = 0
        };

        _mockTableRepository
            .Setup(t => t.DeleteBatchAsync(
                config.TableName,
                It.IsAny<List<DomainListEntry>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Act
        var result = await _orchestrator.ExecuteImportAsync(
            config,
            ImportOperationType.Remove,
            domains,
            progress,
            cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(ImportStatus.Completed);

        _mockTableRepository.Verify(
            t => t.DeleteBatchAsync(
                config.TableName,
                It.IsAny<List<DomainListEntry>>(),
                cts.Token),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteImportAsync_CreatesPartitionKeyForEachDomain()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container",
            BatchSize = 100
        };

        var domains = new[] { "example.com", "test.com", "sample.org" };

        var batchResult = new BatchOperationResult
        {
            BatchId = "batch-1",
            ItemCount = 3,
            SuccessCount = 3,
            FailureCount = 0
        };

        _mockTableRepository
            .Setup(t => t.UpsertBatchAsync(
                config.TableName,
                It.IsAny<List<DomainListEntry>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Act
        await _orchestrator.ExecuteImportAsync(
            config,
            ImportOperationType.Add,
            domains,
            progress,
            cts.Token);

        // Assert
        _mockPartitionKeyStrategy.Verify(
            p => p.GetPartitionKey("example.com"),
            Times.Once);

        _mockPartitionKeyStrategy.Verify(
            p => p.GetPartitionKey("test.com"),
            Times.Once);

        _mockPartitionKeyStrategy.Verify(
            p => p.GetPartitionKey("sample.org"),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteImportAsync_CallsRateLimiter()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container",
            BatchSize = 100
        };

        var domains = new[] { "example.com", "test.com" };

        var batchResult = new BatchOperationResult
        {
            BatchId = "batch-1",
            ItemCount = 2,
            SuccessCount = 2,
            FailureCount = 0
        };

        _mockTableRepository
            .Setup(t => t.UpsertBatchAsync(
                It.IsAny<string>(),
                It.IsAny<List<DomainListEntry>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Act
        await _orchestrator.ExecuteImportAsync(
            config,
            ImportOperationType.Add,
            domains,
            progress,
            cts.Token);

        // Assert
        _mockRateLimiter.Verify(
            r => r.WaitAsync(It.IsAny<int>(), cts.Token),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteImportAsync_CancelledTokenThrowsOperationCanceledException()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container"
        };

        var domains = new[] { "example.com" };
        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Cancelar antes de chamar
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _orchestrator.ExecuteImportAsync(
                config,
                ImportOperationType.Add,
                domains,
                progress,
                cts.Token));
    }

    [Fact]
    public async Task ExecuteImportAsync_ReportsMetricsProgress()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container",
            BatchSize = 1
        };

        var domains = new[] { "example.com", "test.com", "sample.org" };

        var batchResult = new BatchOperationResult
        {
            BatchId = "batch-1",
            ItemCount = 1,
            SuccessCount = 1,
            FailureCount = 0
        };

        _mockTableRepository
            .Setup(t => t.UpsertBatchAsync(
                It.IsAny<string>(),
                It.IsAny<List<DomainListEntry>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var progressUpdates = new List<ImportProgress>();
        var progress = new Progress<ImportProgress>(p => progressUpdates.Add(p));
        var cts = new CancellationTokenSource();

        // Act
        await _orchestrator.ExecuteImportAsync(
            config,
            ImportOperationType.Add,
            domains,
            progress,
            cts.Token);

        // Assert
        progressUpdates.Should().NotBeEmpty();
        progressUpdates.Last().Metrics.Status.Should().Be(ImportStatus.Completed);
    }

    }

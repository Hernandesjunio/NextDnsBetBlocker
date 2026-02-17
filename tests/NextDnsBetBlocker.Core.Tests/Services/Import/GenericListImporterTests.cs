using FluentAssertions;
using Moq;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services.Import;
using Xunit;
using Microsoft.Extensions.Logging;

namespace NextDnsBetBlocker.Core.Tests.Services.Import;

public class GenericListImporterTests
{
    private readonly Mock<IListImportOrchestrator> _mockOrchestrator;
    private readonly Mock<IListBlobRepository> _mockBlobRepository;
    private readonly Mock<IListTableStorageRepository> _mockTableRepository;
    private readonly Mock<ILogger<GenericListImporter>> _mockLogger;
    private readonly GenericListImporter _importer;

    public GenericListImporterTests()
    {
        _mockOrchestrator = new Mock<IListImportOrchestrator>();
        _mockBlobRepository = new Mock<IListBlobRepository>();
        _mockTableRepository = new Mock<IListTableStorageRepository>();
        _mockLogger = new Mock<ILogger<GenericListImporter>>();

        _importer = new GenericListImporter(
            _mockLogger.Object,
            _mockOrchestrator.Object,
            _mockBlobRepository.Object,
            _mockTableRepository.Object);
    }

    [Fact]
    public async Task ImportAsync_WithValidDomains_CallsOrchestratorWithAddOperation()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container"
        };

        var expectedMetrics = new ImportMetrics
        {
            TotalProcessed = 100,
            TotalInserted = 100,
            TotalErrors = 0,
            Status = ImportStatus.Completed
        };

        _mockOrchestrator
            .Setup(o => o.ExecuteImportAsync(
                It.IsAny<ListImportItemConfig>(),
                ImportOperationType.Add,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMetrics);

        _mockBlobRepository
            .Setup(b => b.SaveImportFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("blob-url");

        _mockBlobRepository
            .Setup(b => b.SaveImportMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ImportedListMetadata>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Act
        var result = await _importer.ImportAsync(config, progress, cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(ImportStatus.Completed);
        result.TotalInserted.Should().Be(100);

        _mockOrchestrator.Verify(
            o => o.ExecuteImportAsync(
                config,
                ImportOperationType.Add,
                It.IsAny<IEnumerable<string>>(),
                progress,
                cts.Token),
            Times.Once);

        _mockBlobRepository.Verify(
            b => b.SaveImportFileAsync(
                config.BlobContainer,
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WithErrors_DoesNotSaveToBlob()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container"
        };

        var metricsWithErrors = new ImportMetrics
        {
            TotalProcessed = 100,
            TotalInserted = 95,
            TotalErrors = 5,
            Status = ImportStatus.Failed
        };

        _mockOrchestrator
            .Setup(o => o.ExecuteImportAsync(
                It.IsAny<ListImportItemConfig>(),
                ImportOperationType.Add,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(metricsWithErrors);

        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Act
        var result = await _importer.ImportAsync(config, progress, cts.Token);

        // Assert
        result.TotalErrors.Should().Be(5);
        
        _mockBlobRepository.Verify(
            b => b.SaveImportFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportDiffAsync_CalculatesDiffAndCallsOrchestratorTwiceInParallel()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container"
        };

        var addMetrics = new ImportMetrics
        {
            TotalProcessed = 50,
            TotalInserted = 50,
            TotalErrors = 0,
            Status = ImportStatus.Completed
        };

        var removeMetrics = new ImportMetrics
        {
            TotalProcessed = 20,
            TotalInserted = 20,
            TotalErrors = 0,
            Status = ImportStatus.Completed
        };

        _mockOrchestrator
            .Setup(o => o.ExecuteImportAsync(
                It.IsAny<ListImportItemConfig>(),
                ImportOperationType.Add,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(addMetrics);

        _mockOrchestrator
            .Setup(o => o.ExecuteImportAsync(
                It.IsAny<ListImportItemConfig>(),
                ImportOperationType.Remove,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(removeMetrics);

        _mockBlobRepository
            .Setup(b => b.GetImportMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedListMetadata?)null); // Primeira importação (sem metadata anterior)

        _mockBlobRepository
            .Setup(b => b.SaveImportFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("blob-url");

        _mockBlobRepository
            .Setup(b => b.SaveImportMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ImportedListMetadata>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Act
        var result = await _importer.ImportDiffAsync(config, progress, cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(ImportStatus.Completed);
        result.TotalProcessed.Should().Be(70); // 50 adds + 20 removes

        // Verify que orchestrator foi chamado 2 vezes (Add e Remove)
        _mockOrchestrator.Verify(
            o => o.ExecuteImportAsync(
                config,
                ImportOperationType.Add,
                It.IsAny<IEnumerable<string>>(),
                progress,
                cts.Token),
            Times.Once);

        _mockOrchestrator.Verify(
            o => o.ExecuteImportAsync(
                config,
                ImportOperationType.Remove,
                It.IsAny<IEnumerable<string>>(),
                progress,
                cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task ImportDiffAsync_SavesNewDomainsToBlob()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container"
        };

        var metricsCompleted = new ImportMetrics
        {
            TotalProcessed = 0,
            TotalInserted = 0,
            TotalErrors = 0,
            Status = ImportStatus.Completed
        };

        _mockOrchestrator
            .Setup(o => o.ExecuteImportAsync(
                It.IsAny<ListImportItemConfig>(),
                It.IsAny<ImportOperationType>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(metricsCompleted);

        _mockBlobRepository
            .Setup(b => b.GetImportMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedListMetadata?)null);

        _mockBlobRepository
            .Setup(b => b.SaveImportFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("blob-url");

        _mockBlobRepository
            .Setup(b => b.SaveImportMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ImportedListMetadata>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var progress = new Progress<ImportProgress>();
        var cts = new CancellationTokenSource();

        // Act
        await _importer.ImportDiffAsync(config, progress, cts.Token);

        // Assert
        _mockBlobRepository.Verify(
            b => b.SaveImportFileAsync(
                config.BlobContainer,
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                cts.Token),
            Times.Once);

        _mockBlobRepository.Verify(
            b => b.SaveImportMetadataAsync(
                config.BlobContainer,
                It.IsAny<string>(),
                It.IsAny<ImportedListMetadata>(),
                cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task ImportAsync_CancelledTokenThrowsOperationCanceledException()
    {
        // Arrange
        var config = new ListImportItemConfig
        {
            ListName = "TestList",
            SourceUrl = new[] { "http://example.com/list.txt" },
            TableName = "TestTable",
            BlobContainer = "test-container"
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var progress = new Progress<ImportProgress>();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _importer.ImportAsync(config, progress, cts.Token));
    }
}

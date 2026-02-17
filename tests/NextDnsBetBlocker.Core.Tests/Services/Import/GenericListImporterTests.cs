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
    private readonly Mock<IDownloadService> _mockDownloadService;
    private readonly Mock<ILogger<GenericListImporter>> _mockLogger;
    private readonly GenericListImporter _importer;

    public GenericListImporterTests()
    {
        _mockOrchestrator = new Mock<IListImportOrchestrator>();
        _mockBlobRepository = new Mock<IListBlobRepository>();
        _mockTableRepository = new Mock<IListTableStorageRepository>();
        _mockDownloadService = new Mock<IDownloadService>();
        _mockLogger = new Mock<ILogger<GenericListImporter>>();

        _importer = new GenericListImporter(
            _mockLogger.Object,
            _mockOrchestrator.Object,
            _mockBlobRepository.Object,
            _mockTableRepository.Object,
            _mockDownloadService.Object);
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

        var testDomains = new HashSet<string> { "domain1.com", "domain2.com", "domain3.com" };

        var expectedMetrics = new ImportMetrics
        {
            TotalProcessed = 3,
            TotalInserted = 3,
            TotalErrors = 0,
            Status = ImportStatus.Completed
        };

        // Mock download service
        _mockDownloadService
            .Setup(d => d.DownloadAndParseAsync(
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testDomains);

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
        result.TotalInserted.Should().Be(3);

        // Verify download service was called
        _mockDownloadService.Verify(
            d => d.DownloadAndParseAsync(config.SourceUrl, cts.Token),
            Times.Once);

        // Verify orchestrator was called
        _mockOrchestrator.Verify(
            o => o.ExecuteImportAsync(
                config,
                ImportOperationType.Add,
                It.IsAny<IEnumerable<string>>(),
                progress,
                cts.Token),
            Times.Once);

        // Verify blob save was called
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

        var testDomains = new HashSet<string> { "domain1.com", "domain2.com" };

        var metricsWithErrors = new ImportMetrics
        {
            TotalProcessed = 2,
            TotalInserted = 1,
            TotalErrors = 1,
            Status = ImportStatus.Failed
        };

        // Mock download service
        _mockDownloadService
            .Setup(d => d.DownloadAndParseAsync(
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testDomains);

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
        result.TotalErrors.Should().Be(1);

        // Verify blob save was NOT called (because there were errors)
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

        // New domains from download
        var newDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { "domain1.com", "domain2.com", "domain3.com", "domain4.com", "domain5.com" };

        // Previous domains from blob (simulation of older list)
        var previousDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "domain2.com", "domain3.com", "domain6.com" };

        var addMetrics = new ImportMetrics
        {
            TotalProcessed = 3,
            TotalInserted = 3,
            TotalErrors = 0,
            Status = ImportStatus.Completed
        };

        var removeMetrics = new ImportMetrics
        {
            TotalProcessed = 1,
            TotalInserted = 1,
            TotalErrors = 0,
            Status = ImportStatus.Completed
        };

        // Mock download service - returns new domains
        _mockDownloadService
            .Setup(d => d.DownloadAndParseAsync(
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(newDomains);

        // Mock orchestrator for Add
        _mockOrchestrator
            .Setup(o => o.ExecuteImportAsync(
                It.IsAny<ListImportItemConfig>(),
                ImportOperationType.Add,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(addMetrics);

        // Mock orchestrator for Remove
        _mockOrchestrator
            .Setup(o => o.ExecuteImportAsync(
                It.IsAny<ListImportItemConfig>(),
                ImportOperationType.Remove,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(removeMetrics);

        // Mock blob repository - returns previous metadata (second import)
        _mockBlobRepository
            .Setup(b => b.GetImportMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedListMetadata 
            { 
                ListName = "TestList",
                RecordCount = previousDomains.Count,
                FileHash = "testhash",
                FileSizeBytes = 1000,
                SourceVersion = DateTime.UtcNow.ToString("O")
            });

        // Mock blob repository - returns previous domains stream
        _mockBlobRepository
            .Setup(b => b.GetPreviousImportFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => 
            {
                var content = string.Join("\n", previousDomains);
                return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            });

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
        // 3 adds + 1 remove = 4 total
        result.TotalProcessed.Should().Be(4);

        // Verify orchestrator was called for Add
        _mockOrchestrator.Verify(
            o => o.ExecuteImportAsync(
                config,
                ImportOperationType.Add,
                It.IsAny<IEnumerable<string>>(),
                progress,
                cts.Token),
            Times.Once);

        // Verify orchestrator was called for Remove
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

        var testDomains = new HashSet<string> { "domain1.com", "domain2.com" };

        var metricsCompleted = new ImportMetrics
        {
            TotalProcessed = 0,
            TotalInserted = 0,
            TotalErrors = 0,
            Status = ImportStatus.Completed
        };

        // Mock download service
        _mockDownloadService
            .Setup(d => d.DownloadAndParseAsync(
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testDomains);

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

        // Mock download service to throw OperationCanceledException
        _mockDownloadService
            .Setup(d => d.DownloadAndParseAsync(
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var progress = new Progress<ImportProgress>();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _importer.ImportAsync(config, progress, cts.Token));
    }
}

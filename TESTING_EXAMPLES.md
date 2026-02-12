namespace NextDnsBetBlocker.Core.Tests;

using Microsoft.Extensions.Logging;
using Moq;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services;

/// <summary>
/// Example unit tests for the BetBlockerPipeline.
/// These serve as a template for implementing comprehensive tests.
/// </summary>
public class BetBlockerPipelineTests
{
    private readonly Mock<INextDnsClient> _mockNextDnsClient;
    private readonly Mock<ICheckpointStore> _mockCheckpointStore;
    private readonly Mock<IBlockedDomainStore> _mockBlockedDomainStore;
    private readonly Mock<IHageziProvider> _mockHageziProvider;
    private readonly Mock<IAllowlistProvider> _mockAllowlistProvider;
    private readonly Mock<IBetClassifier> _mockBetClassifier;
    private readonly Mock<ILogger<BetBlockerPipeline>> _mockLogger;

    public BetBlockerPipelineTests()
    {
        _mockNextDnsClient = new Mock<INextDnsClient>();
        _mockCheckpointStore = new Mock<ICheckpointStore>();
        _mockBlockedDomainStore = new Mock<IBlockedDomainStore>();
        _mockHageziProvider = new Mock<IHageziProvider>();
        _mockAllowlistProvider = new Mock<IAllowlistProvider>();
        _mockBetClassifier = new Mock<IBetClassifier>();
        _mockLogger = new Mock<ILogger<BetBlockerPipeline>>();
    }

    private BetBlockerPipeline CreatePipeline()
    {
        return new BetBlockerPipeline(
            _mockNextDnsClient.Object,
            _mockCheckpointStore.Object,
            _mockBlockedDomainStore.Object,
            _mockHageziProvider.Object,
            _mockAllowlistProvider.Object,
            _mockBetClassifier.Object,
            _mockLogger.Object);
    }

    // Example: Test processing logs with no domains to block
    public void ProcessLogs_WithNoDomainsToBlo_ReturnsEmptyStatistics()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var profileId = "test-profile";
        var apiKey = "test-key";

        var logsResponse = new NextDnsLogsResponse
        {
            Data = [],
            Meta = new PaginationMeta { Pagination = new PaginationInfo() }
        };

        _mockNextDnsClient
            .Setup(x => x.GetLogsAsync(profileId, apiKey, null, 1000))
            .ReturnsAsync(logsResponse);

        _mockCheckpointStore
            .Setup(x => x.GetLastTimestampAsync(profileId))
            .ReturnsAsync((DateTime?)null);

        _mockAllowlistProvider
            .Setup(x => x.GetAllowlist())
            .Returns(new HashSet<string>());

        _mockHageziProvider
            .Setup(x => x.GetGamblingDomainsAsync())
            .ReturnsAsync(new HashSet<string>());

        // Act
        // var result = pipeline.ProcessLogsAsync(profileId, apiKey).Result;

        // Assert
        // Assert.AreEqual(0, result.DomainsLogged);
        // Assert.AreEqual(0, result.DomainsBlocked);
    }

    // Example: Test domain blocking
    public void ProcessLogs_WithBetDomains_BlocksThemSuccessfully()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var profileId = "test-profile";
        var apiKey = "test-key";

        var betDomain = "poker-site.com";
        var logsResponse = new NextDnsLogsResponse
        {
            Data = new List<LogEntry>
            {
                new() { Domain = betDomain, Timestamp = DateTime.UtcNow }
            },
            Meta = new PaginationMeta { Pagination = new PaginationInfo() }
        };

        _mockNextDnsClient
            .Setup(x => x.GetLogsAsync(profileId, apiKey, null, 1000))
            .ReturnsAsync(logsResponse);

        _mockNextDnsClient
            .Setup(x => x.AddToDenylistAsync(
                profileId,
                apiKey,
                It.Is<DenylistBlockRequest>(r => r.Id == betDomain)))
            .ReturnsAsync(true);

        _mockCheckpointStore
            .Setup(x => x.GetLastTimestampAsync(profileId))
            .ReturnsAsync((DateTime?)null);

        _mockAllowlistProvider
            .Setup(x => x.GetAllowlist())
            .Returns(new HashSet<string>());

        _mockBlockedDomainStore
            .Setup(x => x.IsBlockedAsync(profileId, betDomain))
            .ReturnsAsync(false);

        _mockHageziProvider
            .Setup(x => x.GetGamblingDomainsAsync())
            .ReturnsAsync(new HashSet<string> { betDomain });

        _mockBetClassifier
            .Setup(x => x.IsBetDomain(betDomain))
            .Returns(true);

        // Act
        // var result = pipeline.ProcessLogsAsync(profileId, apiKey).Result;

        // Assert
        // Assert.AreEqual(1, result.DomainsBlocked);
        // _mockNextDnsClient.Verify(
        //     x => x.AddToDenylistAsync(profileId, apiKey, It.IsAny<DenylistBlockRequest>()),
        //     Times.Once);
    }

    // Example: Test allowlist bypass
    public void ProcessLogs_WithAllowlistedDomain_SkipsBlocking()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var profileId = "test-profile";
        var apiKey = "test-key";

        var allowlistedDomain = "safe-gambling.com";
        var logsResponse = new NextDnsLogsResponse
        {
            Data = new List<LogEntry>
            {
                new() { Domain = allowlistedDomain, Timestamp = DateTime.UtcNow }
            },
            Meta = new PaginationMeta { Pagination = new PaginationInfo() }
        };

        _mockNextDnsClient
            .Setup(x => x.GetLogsAsync(profileId, apiKey, null, 1000))
            .ReturnsAsync(logsResponse);

        _mockCheckpointStore
            .Setup(x => x.GetLastTimestampAsync(profileId))
            .ReturnsAsync((DateTime?)null);

        _mockAllowlistProvider
            .Setup(x => x.GetAllowlist())
            .Returns(new HashSet<string> { allowlistedDomain });

        _mockHageziProvider
            .Setup(x => x.GetGamblingDomainsAsync())
            .ReturnsAsync(new HashSet<string> { allowlistedDomain });

        // Act
        // var result = pipeline.ProcessLogsAsync(profileId, apiKey).Result;

        // Assert
        // Assert.AreEqual(0, result.DomainsBlocked);
        // Assert.AreEqual(1, result.DomainsAllowlisted);
        // _mockNextDnsClient.Verify(
        //     x => x.AddToDenylistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DenylistBlockRequest>()),
        //     Times.Never);
    }
}

/// <summary>
/// Example unit tests for BetClassifier
/// </summary>
public class BetClassifierTests
{
    private readonly Mock<IHageziProvider> _mockHageziProvider;
    private readonly Mock<ILogger<BetClassifier>> _mockLogger;

    public BetClassifierTests()
    {
        _mockHageziProvider = new Mock<IHageziProvider>();
        _mockLogger = new Mock<ILogger<BetClassifier>>();
    }

    private BetClassifier CreateClassifier()
    {
        return new BetClassifier(_mockHageziProvider.Object, _mockLogger.Object);
    }

    // Example: Test exact domain match
    public void IsBetDomain_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var classifier = CreateClassifier();
        var betDomain = "betting-site.com";

        _mockHageziProvider
            .Setup(x => x.GetGamblingDomainsAsync())
            .ReturnsAsync(new HashSet<string> { betDomain });

        // Act
        // var result = classifier.IsBetDomain(betDomain);

        // Assert
        // Assert.IsTrue(result);
    }

    // Example: Test subdomain classification
    public void IsBetDomain_WithBetSubdomain_ReturnsTrue()
    {
        // Arrange
        var classifier = CreateClassifier();
        var baseDomain = "betting-site.com";
        var subDomain = "play.betting-site.com";

        _mockHageziProvider
            .Setup(x => x.GetGamblingDomainsAsync())
            .ReturnsAsync(new HashSet<string> { baseDomain });

        // Act
        // var result = classifier.IsBetDomain(subDomain);

        // Assert
        // Assert.IsTrue(result);
    }

    // Example: Test non-bet domain
    public void IsBetDomain_WithNonBetDomain_ReturnsFalse()
    {
        // Arrange
        var classifier = CreateClassifier();
        var nonBetDomain = "google.com";

        _mockHageziProvider
            .Setup(x => x.GetGamblingDomainsAsync())
            .ReturnsAsync(new HashSet<string> { "bet-site.com" });

        // Act
        // var result = classifier.IsBetDomain(nonBetDomain);

        // Assert
        // Assert.IsFalse(result);
    }
}

/// <summary>
/// Example unit tests for AllowlistProvider
/// </summary>
public class AllowlistProviderTests
{
    private readonly Mock<ILogger<AllowlistProvider>> _mockLogger;
    private readonly string _testFilePath;

    public AllowlistProviderTests()
    {
        _mockLogger = new Mock<ILogger<AllowlistProvider>>();
        _testFilePath = Path.Combine(Path.GetTempPath(), "test-allowlist.txt");
    }

    private AllowlistProvider CreateProvider()
    {
        return new AllowlistProvider(_testFilePath, _mockLogger.Object);
    }

    // Example: Test loading allowlist from file
    public async Task ReloadAsync_WithValidFile_LoadsDomainsSuccessfully()
    {
        // Arrange
        var provider = CreateProvider();
        var allowlistContent = "# Comment\nexample.com\ntest.org\n";
        await File.WriteAllTextAsync(_testFilePath, allowlistContent);

        // Act
        // await provider.ReloadAsync();
        // var allowlist = provider.GetAllowlist();

        // Assert
        // Assert.AreEqual(2, allowlist.Count);
        // Assert.IsTrue(allowlist.Contains("example.com"));
        // Assert.IsTrue(allowlist.Contains("test.org"));

        // Cleanup
        File.Delete(_testFilePath);
    }

    // Example: Test handling missing file
    public async Task ReloadAsync_WithMissingFile_CreatesEmpty()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        // await provider.ReloadAsync();
        // var allowlist = provider.GetAllowlist();

        // Assert
        // Assert.AreEqual(0, allowlist.Count);
    }
}

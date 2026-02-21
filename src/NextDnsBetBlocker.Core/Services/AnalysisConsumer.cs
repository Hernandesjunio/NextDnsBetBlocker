namespace NextDnsBetBlocker.Core.Services;

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Consumidor que analisa domínios suspeitos em detalhes
/// Faz requisições HTTP e análise de conteúdo
/// Envia resultados para Table Storage e cache
/// Bloqueia domínios confirmados no NextDNS
/// </summary>
public class AnalysisConsumer : IAnalysisConsumer
{
    private readonly IGamblingSuspectAnalyzer _analyzer;
    private readonly IGamblingSuspectStore _suspectStore;
    private readonly INextDnsClient _nextDnsClient;
    private readonly IBlockedDomainStore _blockedDomainStore;
    private readonly ILogger<AnalysisConsumer> _logger;

    private long _analyzed;
    private long _blocked;
    private long _whitelisted;
    private long _manualReview;

    public AnalysisConsumer(
        IGamblingSuspectAnalyzer analyzer,
        IGamblingSuspectStore suspectStore,
        INextDnsClient nextDnsClient,
        IBlockedDomainStore blockedDomainStore,
        ILogger<AnalysisConsumer> logger)
    {
        _analyzer = analyzer;
        _suspectStore = suspectStore;
        _nextDnsClient = nextDnsClient;
        _blockedDomainStore = blockedDomainStore;
        _logger = logger;
        _analyzed = 0;
        _blocked = 0;
        _whitelisted = 0;
        _manualReview = 0;
    }

    public async Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("AnalysisConsumer started for profile {ProfileId} with 10 parallel consumer threads", profileId);

            // Reset counters
            _analyzed = 0;
            _blocked = 0;
            _whitelisted = 0;
            _manualReview = 0;

            // Create 10 concurrent consumer tasks
            const int consumerThreadCount = 10;
            var consumerTasks = new Task[consumerThreadCount];

            for (int i = 0; i < consumerThreadCount; i++)
            {
                consumerTasks[i] = ConsumeAndAnalyzeAsync(inputChannel, profileId, cancellationToken);
            }

            // Wait for all consumer threads to complete
            await Task.WhenAll(consumerTasks);

            _logger.LogInformation(
                "AnalysisConsumer completed: Analyzed={Analyzed}, Blocked={Blocked}, Whitelisted={Whitelisted}, ManualReview={ManualReview}",
                _analyzed, _blocked, _whitelisted, _manualReview);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AnalysisConsumer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalysisConsumer failed");
            throw;
        }
    }

    private async Task ConsumeAndAnalyzeAsync(
        Channel<SuspectDomainEntry> inputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        // Read suspects from channel (multiple threads can read simultaneously)
        await foreach (var suspectEntry in inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("Analyzing suspicious domain: {Domain}", suspectEntry.Domain);

                // Perform detailed analysis
                var analysisResult = await _analyzer.AnalyzeDomainAsync(suspectEntry.Domain);

                // Create suspect record for storage
                var suspect = new GamblingSuspect
                {
                    Domain = suspectEntry.Domain,
                    FirstSeen = suspectEntry.FirstSeen,
                    AccessCount = 1,
                    Status = AnalysisStatus.Completed,
                    ConfidenceScore = analysisResult.ConfidenceScore,
                    GamblingIndicators = analysisResult.Indicators.Select(i => $"{i.Category}:{i.Indicator}").ToList(),
                    LastAnalyzed = DateTime.UtcNow,
                    BlockReason = analysisResult.Reason,
                    IsWhitelisted = false,
                    AnalysisDetails = string.Join("; ", analysisResult.Indicators.Select(i => i.Description))
                };

                // Determine status based on score
                if (analysisResult.IsGambling && analysisResult.ConfidenceScore >= 70)
                {
                    suspect.Status = AnalysisStatus.Blocked;
                    suspect.IsWhitelisted = false;

                    // Block in NextDNS
                    var blockSuccess = await _nextDnsClient.AddToDenylistAsync(profileId,
                        new DenylistBlockRequest { Id = suspectEntry.Domain, Active = true });

                    if (blockSuccess)
                    {
                        await _blockedDomainStore.MarkBlockedAsync(profileId, suspectEntry.Domain);
                        Interlocked.Increment(ref _blocked);
                        _logger.LogInformation("✓ Blocked gambling domain {Domain} (Score: {Score})", 
                            suspectEntry.Domain, analysisResult.ConfidenceScore);
                    }
                }
                else if (analysisResult.ConfidenceScore < 40)
                {
                    suspect.Status = AnalysisStatus.Whitelisted;
                    suspect.IsWhitelisted = true;

                    var success = await _nextDnsClient.AddToAllowlistAsync(profileId, suspectEntry.Domain);

                    Interlocked.Increment(ref _whitelisted);
                    _logger.LogInformation("✓ Whitelisted legitimate domain {Domain} (Score: {Score})", 
                        suspectEntry.Domain, analysisResult.ConfidenceScore);
                }
                else
                {
                    // 40-70: needs manual review
                    suspect.Status = AnalysisStatus.Manual_Review;
                    Interlocked.Increment(ref _manualReview);
                    _logger.LogInformation("⚠ Domain {Domain} requires manual review (Score: {Score})", 
                        suspectEntry.Domain, analysisResult.ConfidenceScore);
                }

                // Save to storage
                await _suspectStore.SaveAnalysisResultAsync(suspect);

                Interlocked.Increment(ref _analyzed);

                if (_analyzed % 10 == 0)
                    _logger.LogDebug("Analyzed {Total} suspects", _analyzed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing domain {Domain}", suspectEntry.Domain);
                // Continue with next domain instead of crashing
            }
        }
    }
}

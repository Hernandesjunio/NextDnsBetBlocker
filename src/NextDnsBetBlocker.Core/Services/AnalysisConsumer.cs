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
    }

    public async Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("AnalysisConsumer started for profile {ProfileId}", profileId);

            int analyzed = 0;
            int blocked = 0;
            int whitelisted = 0;
            int manualReview = 0;

            // Read all suspects from input channel (1 thread sequentially)
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
                            blocked++;
                            _logger.LogInformation("✓ Blocked gambling domain {Domain} (Score: {Score})", 
                                suspectEntry.Domain, analysisResult.ConfidenceScore);
                        }
                    }
                    else if (analysisResult.ConfidenceScore < 40)
                    {
                        suspect.Status = AnalysisStatus.Whitelisted;
                        suspect.IsWhitelisted = true;
                        whitelisted++;
                        _logger.LogInformation("✓ Whitelisted legitimate domain {Domain} (Score: {Score})", 
                            suspectEntry.Domain, analysisResult.ConfidenceScore);
                    }
                    else
                    {
                        // 40-70: needs manual review
                        suspect.Status = AnalysisStatus.Manual_Review;
                        manualReview++;
                        _logger.LogInformation("⚠ Domain {Domain} requires manual review (Score: {Score})", 
                            suspectEntry.Domain, analysisResult.ConfidenceScore);
                    }

                    // Save to storage
                    await _suspectStore.SaveAnalysisResultAsync(suspect);

                    analyzed++;

                    if (analyzed % 10 == 0)
                        _logger.LogDebug("Analyzed {Total} suspects", analyzed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error analyzing domain {Domain}", suspectEntry.Domain);
                    // Continue with next domain instead of crashing
                }
            }

            _logger.LogInformation(
                "AnalysisConsumer completed: Analyzed={Analyzed}, Blocked={Blocked}, Whitelisted={Whitelisted}, ManualReview={ManualReview}",
                analyzed, blocked, whitelisted, manualReview);
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
}

namespace NextDnsBetBlocker.Core.Interfaces;

using System.Threading.Channels;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Consumidor de análise da pipeline
/// Analisa domínios suspeitos em detalhes
/// </summary>
public interface IAnalysisConsumer
{
    /// <summary>
    /// Consume suspicious domains and analyze them in detail
    /// Performs HTTP requests and content analysis
    /// </summary>
    Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        string profileId,
        CancellationToken cancellationToken);
}

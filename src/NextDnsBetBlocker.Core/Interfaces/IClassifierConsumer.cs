namespace NextDnsBetBlocker.Core.Interfaces;

using System.Threading.Channels;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Consumidor classificador da pipeline
/// Filtra logs baseado em allowlist, HaGeZi e classificador de apostas
/// </summary>
public interface IClassifierConsumer
{
    /// <summary>
    /// Consume logs, classify them, and forward only suspicious ones
    /// Filters logs based on allowlist, HaGeZi, and BetClassifier
    /// </summary>
    Task StartAsync(
        Channel<SuspectDomainEntry> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken);
}

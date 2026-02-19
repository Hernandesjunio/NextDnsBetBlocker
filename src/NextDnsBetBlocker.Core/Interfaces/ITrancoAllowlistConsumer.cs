namespace NextDnsBetBlocker.Core.Interfaces;

using System.Threading.Channels;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Consumidor de allowlist Tranco
/// Verifica domínios suspeitos contra Tranco List
/// </summary>
public interface ITrancoAllowlistConsumer
{
    /// <summary>
    /// Consume suspicious domains and check against Tranco List
    /// If found → allowlist automatically
    /// If not found → forward for detailed analysis
    /// </summary>
    Task StartAsync(
        Channel<LogEntryData> inputChannel,
        Channel<SuspectDomainEntry> outputChannel,
        string profileId,
        CancellationToken cancellationToken);
}

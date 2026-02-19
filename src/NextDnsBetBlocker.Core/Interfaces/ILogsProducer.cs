namespace NextDnsBetBlocker.Core.Interfaces;

using System.Threading.Channels;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Produtor de logs da pipeline
/// Busca logs do NextDNS e envia para canal
/// </summary>
public interface ILogsProducer
{
    /// <summary>
    /// Start producing logs from NextDNS and send to channel
    /// Runs continuously, pulling logs and writing to channel
    /// </summary>
    Task StartAsync(Channel<LogEntryData> channel, string profileId, CancellationToken cancellationToken);
}

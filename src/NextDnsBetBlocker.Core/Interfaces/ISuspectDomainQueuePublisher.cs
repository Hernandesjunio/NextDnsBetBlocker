namespace NextDnsBetBlocker.Core.Interfaces;

using NextDnsBetBlocker.Core.Models;


/// <summary>
/// Estatísticas da fila
/// </summary>
public class QueueStatistics
{
    /// <summary>
    /// Aproximadamente quantas mensagens estão na fila
    /// </summary>
    public int ApproximateMessageCount { get; set; }

    /// <summary>
    /// Quando foi último acesso
    /// </summary>
    public DateTime LastAccessedTime { get; set; }

    /// <summary>
    /// Quando a fila foi criada
    /// </summary>
    public DateTime CreatedTime { get; set; }
}

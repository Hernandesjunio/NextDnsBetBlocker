namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// OBSOLETE: Esta interface não está sendo utilizada na pipeline atual.
/// Checkpoint está registrado em DI mas nunca é injetado em nenhum serviço ativo.
/// </summary>
[Obsolete("This interface is not used in the current implementation.", false)]
public interface ICheckpointStore
{
    /// <summary>
    /// Gets the last processed timestamp for a profile
    /// </summary>
    Task<DateTime?> GetLastTimestampAsync(string profileId);

    /// <summary>
    /// Updates the last processed timestamp for a profile
    /// </summary>
    Task UpdateLastTimestampAsync(string profileId, DateTime timestamp);
}

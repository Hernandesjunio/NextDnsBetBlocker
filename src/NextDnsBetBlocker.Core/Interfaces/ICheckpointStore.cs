namespace NextDnsBetBlocker.Core.Interfaces;

public interface ICheckpointStore
{
    /// <summary>
    /// Ensures the checkpoint entity exists with default values.
    /// Should be called during application startup to initialize the checkpoint if it doesn't exist.
    /// </summary>
    Task EnsureCheckpointAsync(string profileId);

    /// <summary>
    /// Gets the last processed timestamp for a profile
    /// </summary>
    Task<DateTime?> GetLastTimestampAsync(string profileId);

    /// <summary>
    /// Updates the last processed timestamp for a profile
    /// </summary>
    Task UpdateLastTimestampAsync(string profileId, DateTime timestamp);
}

namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Provides distributed locking mechanism for coordinating work across multiple instances.
/// Useful for preventing concurrent execution of the same task in a distributed environment.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Attempts to acquire a distributed lock.
    /// </summary>
    /// <param name="lockName">Unique identifier for the lock</param>
    /// <param name="lockDurationSeconds">How long the lock should be held (lease timeout)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if lock was acquired, false if already held by another instance</returns>
    Task<bool> TryAcquireLockAsync(string lockName, int lockDurationSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a previously acquired distributed lock.
    /// </summary>
    /// <param name="lockName">Unique identifier for the lock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completed task</returns>
    Task ReleaseLockAsync(string lockName, CancellationToken cancellationToken = default);
}

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

namespace NextDnsBetBlocker.Core.Services.Synchronization;

/// <summary>
/// Implements distributed locking using Azure Blob Storage Leases.
/// 
/// How it works:
/// - Creates/uses a blob for each lock
/// - Acquires a lease on the blob (acts as lock)
/// - Lease automatically expires after specified duration
/// - Multiple instances compete for the same lease
/// - Only one instance can hold the lease at a time
/// 
/// Thread-safe for multi-instance scenarios (scale-out).
/// </summary>
public class BlobStorageDistributedLock : IDistributedLockProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<BlobStorageDistributedLock> _logger;
    private readonly Dictionary<string, string> _heldLeases = [];

    public BlobStorageDistributedLock(
        BlobContainerClient containerClient,
        ILogger<BlobStorageDistributedLock> logger)
    {
        ArgumentNullException.ThrowIfNull(containerClient);
        ArgumentNullException.ThrowIfNull(logger);

        _containerClient = containerClient;
        _logger = logger;
        _containerClient.CreateIfNotExists();
    }

    public async Task<bool> TryAcquireLockAsync(
        string lockName,
        int lockDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(lockName);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockDurationSeconds, 0);

            var blobClient = _containerClient.GetBlobClient(lockName);

            // Ensure blob exists
            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                try
                {
                    await blobClient.UploadAsync(
                        BinaryData.FromString("lock"),
                        overwrite: true,
                        cancellationToken: cancellationToken);
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 409)
                {
                    // Blob was just created by another instance - proceed to try lease
                }
            }

            // Try to acquire lease
            var leaseClient = blobClient.GetBlobLeaseClient();
            var leaseDuration = TimeSpan.FromSeconds(lockDurationSeconds);

            try
            {
                var lease = await leaseClient.AcquireAsync(leaseDuration, cancellationToken: cancellationToken);
                _heldLeases[lockName] = lease.Value.LeaseId;

                _logger.LogDebug(
                    "Distributed lock acquired: {lockName} (LeaseId: {leaseId}, Duration: {duration}s)",
                    lockName,
                    lease.Value.LeaseId[..8],
                    lockDurationSeconds);

                return true;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409)
            {
                // Lease already held by another instance
                _logger.LogDebug("Distributed lock already held by another instance: {lockName}", lockName);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring distributed lock: {lockName}", lockName);
            throw;
        }
    }

    public async Task ReleaseLockAsync(string lockName, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(lockName);

            if (!_heldLeases.TryGetValue(lockName, out var leaseId))
            {
                _logger.LogWarning("No held lease found for lock: {lockName}", lockName);
                return;
            }

            var blobClient = _containerClient.GetBlobClient(lockName);
            var leaseClient = blobClient.GetBlobLeaseClient(leaseId);

            try
            {
                await leaseClient.ReleaseAsync(cancellationToken: cancellationToken);
                _heldLeases.Remove(lockName);

                _logger.LogDebug("Distributed lock released: {lockName}", lockName);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409)
            {
                // Lease already expired or released
                _logger.LogWarning("Lease already expired or released: {lockName}", lockName);
                _heldLeases.Remove(lockName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing distributed lock: {lockName}", lockName);
            throw;
        }
    }
}

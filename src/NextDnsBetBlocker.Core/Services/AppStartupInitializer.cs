namespace NextDnsBetBlocker.Core.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Centralizes all application startup initialization logic.
/// Handles checkpoint seeding and storage infrastructure initialization.
/// Called once at application startup to ensure all prerequisites are met.
/// </summary>
[Obsolete("This class is deprecated. Checkpoint seeding and storage initialization are now handled automatically by the respective services. Manual initialization is no longer necessary.", true)]
public class AppStartupInitializer
{
    private readonly ICheckpointStore _checkpointStore;
    private readonly IGamblingSuspectStore _suspectStore;
    private readonly IStorageInfrastructureInitializer _storageInitializer;
    private readonly ILogger<AppStartupInitializer> _logger;
    private readonly WorkerSettings _settings;

    public AppStartupInitializer(
        ICheckpointStore checkpointStore,
        IGamblingSuspectStore suspectStore,
        IStorageInfrastructureInitializer storageInitializer,
        ILogger<AppStartupInitializer> logger,
        IOptions<WorkerSettings> options)
    {
        _checkpointStore = checkpointStore;
        _suspectStore = suspectStore;
        _storageInitializer = storageInitializer;
        _logger = logger;
        _settings = options.Value;
    }

    /// <summary>
    /// Executes all startup initialization steps.
    /// Logs warnings if individual steps fail, but allows startup to continue.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting application startup initialization");

        try
        {
            // Initialize storage infrastructure (tables and containers)
            await InitializeStorageInfrastructureAsync(cancellationToken);

            // Seed checkpoint if needed
            await InitializeCheckpointAsync(cancellationToken);

            // Initialize gambling suspects table
            await InitializeGamblingSuspectStoreAsync(cancellationToken);

            _logger.LogInformation("Application startup initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during application startup initialization");
            throw;
        }
    }

    private async Task InitializeStorageInfrastructureAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing storage infrastructure (tables and containers)");
            await _storageInitializer.InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize storage infrastructure");
            throw;
        }
    }

    private async Task InitializeCheckpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Ensuring checkpoint exists for profile {ProfileId}", _settings.NextDnsProfileId);
            await _checkpointStore.EnsureCheckpointAsync(_settings.NextDnsProfileId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize checkpoint");
        }
    }

    private async Task InitializeGamblingSuspectStoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing gambling suspects table");
            await _suspectStore.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize gambling suspects table");
        }
    }
}

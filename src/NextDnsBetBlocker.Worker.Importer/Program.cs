using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services.Import;
using NextDnsBetBlocker.Core.Services.Storage;

/// <summary>
/// NextDnsBetBlocker.Worker.Importer
/// 
/// Runs on LOCAL MACHINE (bare metal)
/// Responsible for:
/// - Initial import of 5M Tranco domains
/// - Periodic diff imports (weekly)
/// - Publishing suspicious domains to Azure Queue
/// 
/// Does NOT run analysis or blocking (cloud-only)
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var host = new HostBuilder()
            .ConfigureAppConfiguration((context, configBuilder) =>
            {
                configBuilder.AddConfiguration(config);
            })
            .ConfigureServices((context, services) =>
            {
                var settings = context.Configuration.Get<ImporterSettings>() ?? new ImporterSettings();

                // ============= AZURE STORAGE SETUP =============
                if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
                {
                    var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
                    var blobServiceClient = new BlobServiceClient(settings.AzureStorageConnectionString);

                    services.AddSingleton(tableServiceClient);
                    services.AddSingleton(blobServiceClient);
                }
                else
                {
                    throw new InvalidOperationException("AzureStorageConnectionString is required");
                }

                // ============= MEMORY CACHE =============
                services.AddMemoryCache();

                // ============= IMPORT INFRASTRUCTURE =============
                services.AddSingleton<IImportMetricsCollector, ImportMetricsCollector>();
                services.AddSingleton<IPartitionKeyStrategy>(sp => new PartitionKeyStrategy(10));
                services.AddSingleton<IImportRateLimiter>(sp => new ImportRateLimiter(150000));

                // ============= HTTP CLIENT =============
                services.AddHttpClient<IListImportProducer, ListImportProducer>();

                // ============= IMPORT CONSUMER & ORCHESTRATOR =============
                services.AddSingleton<IListImportConsumer, ListImportConsumer>();
                services.AddSingleton<IListImportOrchestrator, ListImportOrchestrator>();

                // ============= STORAGE REPOSITORIES =============
                services.AddSingleton<IListTableStorageRepository>(sp =>
                {
                    var connString = settings.AzureStorageConnectionString;
                    return new ListTableStorageRepository(
                        connString,
                        "TrancoList",
                        sp.GetRequiredService<ILogger<ListTableStorageRepository>>());
                });

                services.AddSingleton<IListBlobRepository>(sp =>
                {
                    var connString = settings.AzureStorageConnectionString;
                    return new ListBlobRepository(
                        connString,
                        "tranco-lists",
                        sp.GetRequiredService<ILogger<ListBlobRepository>>());
                });

                // ============= LIST TABLE PROVIDER (with cache) =============
                services.AddSingleton<IListTableProvider>(sp =>
                {
                    var connString = settings.AzureStorageConnectionString;
                    var tableServiceClient = new TableServiceClient(connString);
                    var tableClient = tableServiceClient.GetTableClient("TrancoList");
                    var cache = sp.GetRequiredService<IMemoryCache>();
                    var partitionStrategy = sp.GetRequiredService<IPartitionKeyStrategy>();
                    return new ListTableProvider(
                        tableClient,
                        cache,
                        partitionStrategy,
                        sp.GetRequiredService<ILogger<ListTableProvider>>());
                });

                // ============= GENERIC IMPORTER =============
                services.AddSingleton<IListImporter>(sp =>
                {
                    return new GenericListImporter(
                        sp.GetRequiredService<ILogger<GenericListImporter>>(),
                        sp.GetRequiredService<IListImportOrchestrator>(),
                        sp.GetRequiredService<IListBlobRepository>(),
                        sp.GetRequiredService<IListTableStorageRepository>());
                });

                // ============= STORAGE INFRASTRUCTURE INITIALIZER =============
                services.AddSingleton<IStorageInfrastructureInitializer>(sp =>
                {
                    var tableRepo = sp.GetRequiredService<IListTableStorageRepository>();
                    var blobClient = string.IsNullOrEmpty(settings.AzureStorageConnectionString)
                        ? null
                        : new BlobServiceClient(settings.AzureStorageConnectionString)
                            .GetBlobContainerClient("default");

                    return new StorageInfrastructureInitializer(
                        tableRepo,
                        settings.AzureStorageConnectionString,
                        sp.GetRequiredService<ILogger<StorageInfrastructureInitializer>>());
                });

                // ============= TRANCO LIST IMPORTER =============
                services.AddSingleton<TrancoListImporter>();
                var trancoConfig = TrancoListImporter.CreateConfig();
                services.AddSingleton(trancoConfig);

                // ============= TRANCO ALLOW LIST PROVIDER =============
                services.AddSingleton<ITrancoAllowlistProvider, TrancoAllowlistProvider>();

                // ============= IMPORT BACKGROUND SERVICE =============
                services.AddHostedService<ImportListBackgroundService>();

                // ============= SETTINGS =============
                services.AddSingleton(settings);
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddDebug();

                var logLevel = context.Configuration.GetSection("Logging:LogLevel:Default").Value ?? "Information";
                if (Enum.TryParse<LogLevel>(logLevel, out var level))
                {
                    logging.SetMinimumLevel(level);
                }
            })
            .Build();

        // ============= INITIALIZE STORAGE INFRASTRUCTURE =============
        try
        {
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Initializing storage infrastructure for Importer...");

            var storageInit = host.Services.GetRequiredService<IStorageInfrastructureInitializer>();
            await storageInit.InitializeAsync();

            logger.LogInformation("Storage infrastructure initialized successfully");
        }
        catch (Exception ex)
        {
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("StorageInitialization");
            logger.LogError(ex, "Failed to initialize storage infrastructure");
            throw;
        }
        // ============= END STORAGE INITIALIZATION =============

        await host.RunAsync();
    }
}

/// <summary>
/// Settings specific to Importer worker
/// </summary>
public class ImporterSettings
{
    public string AzureStorageConnectionString { get; set; } = string.Empty;
}

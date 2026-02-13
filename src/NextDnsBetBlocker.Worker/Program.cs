using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Services;
using NextDnsBetBlocker.Worker;
using NextDnsBetBlocker.Worker.Services;

public static class Program
{
    private static TableClient? _checkpointTableClient;

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
                var settings = context.Configuration.Get<WorkerSettings>() ?? new WorkerSettings();

                // HttpClientFactory - manages connection pooling and reuse
                services.AddHttpClient<INextDnsClient, NextDnsClient>();
                services.AddHttpClient("HageziProvider")
                    .ConfigureHttpClient(client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);
                    });

                // Azure Storage
                if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
                {
                    var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
                    var tableClient = tableServiceClient.GetTableClient("BlockedDomains");
                    var checkpointTableClient = tableServiceClient.GetTableClient("AgentState");
                    var suspectTableClient = tableServiceClient.GetTableClient("GamblingSuspects");

                    tableClient.CreateIfNotExists();
                    checkpointTableClient.CreateIfNotExists();
                    suspectTableClient.CreateIfNotExists();

                    _checkpointTableClient = checkpointTableClient;

                    services.AddSingleton(tableClient);
                    services.AddSingleton(suspectTableClient);
                    services.AddSingleton<IBlockedDomainStore>(sp => new BlockedDomainStore(tableClient, sp.GetRequiredService<ILogger<BlockedDomainStore>>()));
                    services.AddSingleton<ICheckpointStore>(sp => new CheckpointStore(checkpointTableClient, sp.GetRequiredService<ILogger<CheckpointStore>>()));
                    services.AddSingleton<IGamblingSuspectStore>(sp => new GamblingSuspectStore(suspectTableClient, sp.GetRequiredService<ILogger<GamblingSuspectStore>>()));
                }
                else
                {
                    // Use local file storage for development
                    services.AddSingleton<IBlockedDomainStore>(sp => new LocalBlockedDomainStore(sp.GetRequiredService<ILogger<LocalBlockedDomainStore>>()));
                    services.AddSingleton<ICheckpointStore>(sp => new LocalCheckpointStore(sp.GetRequiredService<ILogger<LocalCheckpointStore>>()));
                }

                // Blob Storage for HaGeZi
                BlobContainerClient containerClient;
                if (settings.UseBlobStorage && !string.IsNullOrEmpty(settings.AzureStorageConnectionString))
                {
                    var blobServiceClient = new BlobServiceClient(settings.AzureStorageConnectionString);
                    containerClient = blobServiceClient.GetBlobContainerClient("hagezi-gambling");
                }
                else
                {
                    // Use local filesystem for development
                    var localPath = Path.Combine(Directory.GetCurrentDirectory(), "data");
                    Directory.CreateDirectory(localPath);
                    containerClient = new LocalBlobContainerClient(localPath);
                }

                services.AddSingleton(containerClient);
                services.AddSingleton<IHageziProvider>(sp =>
                    new HageziProvider(
                        containerClient,
                        Path.Combine(Directory.GetCurrentDirectory(), "data", "hagezi-gambling-domains.txt"),
                        sp.GetRequiredService<IHttpClientFactory>(),
                        sp.GetRequiredService<ILogger<HageziProvider>>()));

                // Allowlist
                var allowlistPath = Path.Combine(Directory.GetCurrentDirectory(), "allowlist.txt");
                services.AddSingleton<IAllowlistProvider>(sp =>
                    new AllowlistProvider(allowlistPath, sp.GetRequiredService<ILogger<AllowlistProvider>>()));

                // Classifier and Pipeline
                services.AddSingleton<IBetClassifier, BetClassifier>();
                services.AddSingleton<IBetBlockerPipeline>(sp =>
                    new BetBlockerPipeline(
                        sp.GetRequiredService<INextDnsClient>(),
                        sp.GetRequiredService<ICheckpointStore>(),
                        sp.GetRequiredService<IBlockedDomainStore>(),
                        sp.GetRequiredService<IHageziProvider>(),
                        sp.GetRequiredService<IAllowlistProvider>(),
                        sp.GetRequiredService<IBetClassifier>(),
                        sp.GetRequiredService<ILogger<BetBlockerPipeline>>(),
                        settings.RateLimitPerSecond));

                // Seeder
                services.AddSingleton<BlockedDomainsSeeder>();

                // Gambling Suspect Analyzer
                services.AddSingleton<IGamblingSuspectAnalyzer, GamblingSuspectAnalyzer>();

                // Worker
                services.AddSingleton<WorkerService>();
                services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<WorkerService>());

                // Settings
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

        // Seed checkpoint data before running
        if (_checkpointTableClient != null)
        {
            await SeedCheckpointAsync(_checkpointTableClient);
        }

        // Initialize GamblingSuspects table
        try
        {
            var suspectStore = host.Services.GetRequiredService<IGamblingSuspectStore>();
            await suspectStore.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Log but don't fail startup if suspect table init fails
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("GamblingSuspectInitialization");
            logger.LogWarning(ex, "Failed to initialize GamblingSuspects table");
        }

        // Seed blocked domains from file (only once)
        //var blockedDomainsFile = Path.Combine(Directory.GetCurrentDirectory(), "data", "blocked.txt");
        //await seeder.SeedBlockedDomainsAsync(settings.NextDnsProfileId, blockedDomainsFile);

        await host.RunAsync();
    }

    private static async Task SeedCheckpointAsync(TableClient checkpointTableClient)
    {
        try
        {
            var response = await checkpointTableClient.GetEntityAsync<TableEntity>("checkpoint", "71cb47");
            // Entity exists, no need to seed
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Entity doesn't exist, create it with a default timestamp
            var defaultTimestamp = DateTime.UtcNow.AddDays(-1);
            var entity = new TableEntity("checkpoint", "71cb47")
            {
                { "LastTimestamp", defaultTimestamp }
            };
            await checkpointTableClient.AddEntityAsync(entity);
        }
    }
}
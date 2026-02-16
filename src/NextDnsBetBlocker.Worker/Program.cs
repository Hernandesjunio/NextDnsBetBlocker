global using NextDnsBetBlocker.Worker;
global using NextDnsBetBlocker.Worker.Services;

using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.DependencyInjection;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services;

/// <summary>
/// NextDnsBetBlocker.Worker
/// 
/// Runs on AZURE CLOUD
/// Responsible for:
/// - Fetching NextDNS logs
/// - Classifying domains (using Tranco cache)
/// - Publishing suspicious domains to Azure Queue for analysis/blocking
/// 
/// Does NOT run import (local-only on Importer worker)
/// 
/// DI Registration: Centralized in Core.DependencyInjection.CoreServiceCollectionExtensions
/// </summary>
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
                // ============= CENTRALIZED CORE DI =============
                // All dependency injection is now in CoreServiceCollectionExtensions
                services.AddCoreServices(context.Configuration, ServiceLayerType.Analysis);

                // ============= WORKER SETTINGS (IOptions) =============
                services.AddOptions<WorkerSettings>()
                    .Bind(context.Configuration.GetSection("WorkerSettings"))
                    .ValidateOnStart();

                // ============= WORKER-SPECIFIC SERVICES =============
                services.AddSingleton<BlockedDomainsSeeder>();
                services.AddSingleton<WorkerService>();
                services.AddSingleton<IHostedService,WorkerService>();

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

        var settings = host.Services.GetRequiredService<IOptions<WorkerSettings>>().Value; ;

        // Store checkpoint client for seeding                
        if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
        {
            var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
            _checkpointTableClient = tableServiceClient.GetTableClient("AgentState");

        }
        // ============= SEED CHECKPOINT DATA =============
        if (_checkpointTableClient != null)
        {
            await SeedCheckpointAsync(_checkpointTableClient);
        }

        // ============= INITIALIZE GAMBLING SUSPECTS TABLE =============
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

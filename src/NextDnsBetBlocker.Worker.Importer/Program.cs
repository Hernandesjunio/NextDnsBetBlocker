using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.DependencyInjection;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// NextDnsBetBlocker.Worker.Importer
/// 
/// Runs on LOCAL MACHINE (bare metal)
/// Responsible for:
/// - Initial import of 5M Tranco domains
/// - Periodic diff imports (weekly)
/// 
/// Does NOT run analysis or blocking (cloud-only)
/// 
/// DI Registration: Centralized in Core.DependencyInjection.CoreServiceCollectionExtensions
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
                // ============= CENTRALIZED CORE DI =============
                // All dependency injection is now in CoreServiceCollectionExtensions
                services.AddCoreServices(context.Configuration, ServiceLayerType.Importer);
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
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Program");
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

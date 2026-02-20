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
    public static async Task Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration((context, configBuilder) =>
            {
                configBuilder
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                    .AddUserSecrets("0ff93d8b-998c-49a6-b6c7-b487f46e236f")
                    .AddEnvironmentVariables();
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
                services.AddSingleton<IHostedService, WorkerService>();
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

        // ============= INITIALIZATION ON STARTUP =============
        using (var scope = host.Services.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<AppStartupInitializer>();
            await initializer.InitializeAsync();
        }

        await host.RunAsync();
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.DependencyInjection;
using NextDnsBetBlocker.Core.Services.Import;

/// <summary>
/// NextDnsBetBlocker.Worker.Importer
/// 
/// Console App que roda APENAS UMA VEZ (via ACI)
/// Executa importação sequencial: Hagezi → Tranco → Encerra
/// 
/// Frequency: 1x/semana (domingo 00:00)
/// Orquestração: Azure Scheduler → ACI
/// Duration: ~15 min
/// Custo: ~R$ 1.20/mês
/// 
/// DI Registration: Centralizado em CoreServiceCollectionExtensions
/// </summary>

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();

// ============= LOGGING SETUP =============
services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();

    var logLevel = config.GetSection("Logging:LogLevel:Default").Value ?? "Information";
    if (Enum.TryParse<LogLevel>(logLevel, out var level))
    {
        logging.SetMinimumLevel(level);
    }
});

// ============= CONFIGURATION =============
services.AddSingleton(config);

// ============= CENTRALIZED CORE DI =============
services.AddCoreServices(config, ServiceLayerType.Importer);

// ============= PIPELINE & FACTORY =============
services.AddSingleton<IListImporterFactory, ListImporterFactory>();
services.AddSingleton<ImportListPipeline>();

// ============= BUILD SERVICE PROVIDER =============
var serviceProvider = services.BuildServiceProvider();

// ============= RUN PIPELINE =============
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var pipeline = serviceProvider.GetRequiredService<ImportListPipeline>();

logger.LogInformation("═══════════════════════════════════════");
logger.LogInformation("   NextDnsBetBlocker Import Worker");
logger.LogInformation("   Running in ACI (Azure Container)");
logger.LogInformation("═══════════════════════════════════════");

using var cts = new CancellationTokenSource();

// Handle Ctrl+C gracefully
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    logger.LogInformation("Shutdown signal received, gracefully stopping...");
    cts.Cancel();
};

// Execute pipeline (runs once and returns)
var result = await pipeline.ExecuteAsync(cts.Token);

if (result.Success)
{
    logger.LogInformation("✓ Import Pipeline completed successfully");
    Environment.Exit(0);
}
else
{
    logger.LogError("✗ Import Pipeline failed: {Error}", result.ErrorMessage ?? "Unknown error");
    Environment.Exit(1);
}

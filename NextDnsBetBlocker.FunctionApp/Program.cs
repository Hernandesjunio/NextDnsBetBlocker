global using NextDnsBetBlocker.Core.DependencyInjection;
global using NextDnsBetBlocker.Core.Interfaces;
global using NextDnsBetBlocker.Core.Models;

using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Services;
using NextDnsBetBlocker.FunctionApp;

/// <summary>
/// NextDnsBetBlocker.FunctionApp
/// 
/// Runs on AZURE CLOUD
/// Responsible for:
/// - Fetching NextDNS logs (via TimerTrigger)
/// - Classifying domains (using Tranco cache)
/// - Publishing suspicious domains to Azure Queue for analysis/blocking
/// 
/// Same scope as Worker, but deployed as Azure Functions
/// 
/// DI Registration: Centralized in Core.DependencyInjection.CoreServiceCollectionExtensions
/// </summary>

var builder = FunctionsApplication.CreateBuilder(args);

// ============= CONFIGURATION =============
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddUserSecrets("0ff93d8b-998c-49a6-b6c7-b487f46e236f")
    .AddEnvironmentVariables();

// ============= DEPENDENCY INJECTION =============
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// ============= CENTRALIZED CORE DI =============
builder.Services.AddCoreServices(builder.Configuration, ServiceLayerType.Analysis);

// ============= WORKER SETTINGS (IOptions) =============
builder.Services.AddOptions<WorkerSettings>()
    .Bind(builder.Configuration.GetSection("WorkerSettings"))
    .ValidateOnStart();

// ============= FUNCTION-SPECIFIC SERVICES =============
builder.Services.AddSingleton<AnalysisFunction>();

// ============= LOGGING =============
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();
    var logLevel = builder.Configuration.GetSection("Logging:LogLevel:Default").Value ?? "Information";
    if (Enum.TryParse<LogLevel>(logLevel, out var level))
    {
        logging.SetMinimumLevel(level);
    }
});

var app = builder.Build();

// ============= INITIALIZATION ON STARTUP =============
using (var scope = app.Services.CreateScope())
{
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var settings = scope.ServiceProvider.GetRequiredService<IOptions<WorkerSettings>>().Value;

    // Initialize checkpoint in table storage
    if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
    {
        var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
        var checkpointTableClient = tableServiceClient.GetTableClient("AgentState");
        await SeedCheckpointAsync(checkpointTableClient, configuration);
    }

    // Initialize gambling suspects table
    try
    {
        var suspectStore = scope.ServiceProvider.GetRequiredService<IGamblingSuspectStore>();
        await suspectStore.InitializeAsync();
    }
    catch (Exception ex)
    {
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("GamblingSuspectInitialization");
        logger.LogWarning(ex, "Failed to initialize GamblingSuspects table");
    }
}

await app.RunAsync();

/// <summary>
/// Seeds the checkpoint entity in Azure Table Storage if it doesn't exist
/// </summary>
static async Task SeedCheckpointAsync(TableClient checkpointTableClient, IConfiguration configuration)
{
    var nextDnsProfileId = configuration["WorkerSettings:NextDnsProfileId"];
    ArgumentNullException.ThrowIfNull(nextDnsProfileId);

    try
    {
        await checkpointTableClient.CreateIfNotExistsAsync();
        await checkpointTableClient.GetEntityAsync<TableEntity>("checkpoint", nextDnsProfileId);
        // Entity exists, no need to seed
    }
    catch (Azure.RequestFailedException ex) when (ex.Status == 404)
    {
        // Entity doesn't exist, create it with a default timestamp
        var defaultTimestamp = DateTime.UtcNow.AddDays(-1);
        var entity = new TableEntity("checkpoint", nextDnsProfileId)
        {
            { "LastTimestamp", defaultTimestamp }
        };
        await checkpointTableClient.AddEntityAsync(entity);
    }
}

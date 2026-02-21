global using NextDnsBetBlocker.Core.DependencyInjection;
global using NextDnsBetBlocker.Core.Interfaces;
global using NextDnsBetBlocker.Core.Models;

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

await app.RunAsync();

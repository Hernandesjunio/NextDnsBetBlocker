namespace NextDnsBetBlocker.Core.DependencyInjection;

using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services;
using NextDnsBetBlocker.Core.Services.Import;
using NextDnsBetBlocker.Core.Services.Storage;

/// <summary>
/// Centraliza registro de DI para todas as camadas (Importer e Analysis)
/// Evita duplicação entre Worker.Importer e Worker
/// Single source of truth para configuração de serviços
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os serviços da Core baseado no tipo de camada
    /// </summary>
    public static IServiceCollection AddCoreServices(
        this IServiceCollection services,
        IConfiguration configuration,
        ServiceLayerType layerType)
    {
        // ============= SHARED SERVICES (ambas camadas) =============
        RegisterSharedServices(services, configuration);

        // ============= LAYER-SPECIFIC SERVICES =============
        switch (layerType)
        {
            case ServiceLayerType.Importer:
                RegisterImporterServices(services, configuration);
                break;

            case ServiceLayerType.Analysis:
                RegisterAnalysisServices(services, configuration);
                break;

            default:
                throw new InvalidOperationException($"Unknown service layer type: {layerType}");
        }

        return services;
    }


    /// <summary>
    /// Serviços compartilhados por ambas camadas
    /// </summary>
    private static void RegisterSharedServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IPartitionKeyStrategy>(sp => new PartitionKeyStrategy(10));
        services.AddSingleton<IListTableProvider, ListTableProvider>();
        services.AddSingleton(c => new TableServiceClient(c.GetRequiredService<IOptions<WorkerSettings>>().Value.AzureStorageConnectionString));
        services.AddSingleton(c => new BlobServiceClient(c.GetRequiredService<IOptions<WorkerSettings>>().Value.AzureStorageConnectionString));

        // ============= MEMORY CACHE =============
        services.AddMemoryCache();

        services.AddScoped<IProgressReporter, LoggingProgressReporter>();

        // ============= STORAGE INFRASTRUCTURE INITIALIZER =============
        services.AddSingleton<IStorageInfrastructureInitializer, StorageInfrastructureInitializer>();
    }

    /// <summary>
    /// Serviços específicos para a camada IMPORTER (local)
    /// </summary>
    private static void RegisterImporterServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // ============= HTTP CLIENTS =============
        services.AddHttpClient("HttpDownloadService")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        // ============= IMPORT INFRASTRUCTURE =============
        services.AddSingleton<IImportMetricsCollector, ImportMetricsCollector>();

        services.AddSingleton<IImportRateLimiter>(sp => new ImportRateLimiter(150000));

        // ============= LIST IMPORT CONFIGS =============
        // Register ListImportConfig (mestre) with Items collection
        services.AddOptions<ListImportConfig>()
            .Bind(configuration.GetSection("ListImport"))
            .ValidateOnStart();

        // Register IEnumerable<ListImportItemConfig> for consumers
        services.AddSingleton<IEnumerable<ListImportItemConfig>>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ListImportConfig>>().Value;
            return config.Items ?? Array.Empty<ListImportItemConfig>();
        });

        // ============= CHECKPOINT STORE =============
        // ICheckpointStore é registrado em RegisterSharedServices

        // ============= PARALLEL IMPORT CONFIG =============
        services.AddOptions<ParallelImportConfig>()
            .Bind(configuration.GetSection("ParallelImport"))
            .ValidateOnStart();

        // Registrar ParallelImportConfig como singleton para injeção direta
        services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<ParallelImportConfig>>().Value);

        services.AddSingleton<ParallelImportConfig>(sp =>
            sp.GetRequiredService<IOptionsSnapshot<ParallelImportConfig>>().Value);

        // ============= IMPORT ORCHESTRATOR =============
        services.AddSingleton<IListImportOrchestrator, ListImportOrchestrator>();

        // ============= DOWNLOAD SERVICE =============
        services.AddSingleton<IDownloadService, HttpDownloadService>();

        // ============= PARTITION KEY STRATEGY & LIST TABLE PROVIDER =============


        // ============= STORAGE REPOSITORIES =============
        services.AddSingleton<IListTableStorageRepository, ListTableStorageRepository>();

        services.AddSingleton<IListBlobRepository, ListBlobRepository>();

        // ============= TRANCO ALLOW LIST PROVIDER =============
        services.AddSingleton<ITrancoAllowlistProvider, TrancoAllowlistProvider>();

        // ============= GENERIC LIST IMPORTER =============
        services.AddSingleton<GenericListImporter>(sp =>
        {
            return new GenericListImporter(
                sp.GetRequiredService<ILogger<GenericListImporter>>(),
                sp.GetRequiredService<IListImportOrchestrator>(),
                sp.GetRequiredService<IListBlobRepository>(),
                sp.GetRequiredService<IListTableStorageRepository>(),
                sp.GetRequiredService<IDownloadService>());
        });

        // ============= GENERIC LIST IMPORTER (as IListImporter) =============
        services.AddSingleton<IListImporter>(sp =>
            sp.GetRequiredService<GenericListImporter>());
               
        // ============= HAGEZI PROVIDER CONFIG (with IOptions) =============
        services.AddOptions<HageziProviderConfig>()
            .Bind(configuration.GetSection("HaGeZi"))
            .ValidateOnStart();

        // Register HageziProvider for Importer layer
        services.AddSingleton<IHageziProvider, HageziProvider>();
    }

    /// <summary>
    /// Serviços específicos para a camada ANALYSIS (cloud)
    /// </summary>
    private static void RegisterAnalysisServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // ============= WORKER SETTINGS (IOptions) =============
        services.AddOptions<WorkerSettings>()
            .Bind(configuration.GetSection("WorkerSettings"))
            .ValidateOnStart();

        // ============= HTTP CLIENTS =============
        services.AddHttpClient("HageziProvider")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        // ============= HAGEZI PROVIDER CONFIG (with IOptions) =============
        services.AddOptions<HageziProviderConfig>()
            .Bind(configuration.GetSection("HaGeZi"))
            .ValidateOnStart();

        // ============= AZURE STORAGE - TABLE CLIENTS =============
        services.AddSingleton<IGamblingSuspectStore, GamblingSuspectStore>();

        // ============= HAGEZI GAMBLING STORE (Table Storage Query) =============
        services.AddSingleton<IHageziGamblingStore, HageziGamblingStore>();


        // ============= BLOB STORAGE FOR HAGEZI =============        
        services.AddSingleton<IHageziProvider, HageziProvider>();

        // ============= CLASSIFIER =============
        services.AddSingleton<IBetClassifier, BetClassifier>();

        // ============= PIPELINE COMPONENTS =============
        services.AddSingleton<ILogsProducer, LogsProducer>();
        services.AddSingleton<IClassifierConsumer, ClassifierConsumer>();
        services.AddSingleton<ITrancoAllowlistConsumer, TrancoAllowlistConsumer>();
        services.AddSingleton<IAnalysisConsumer, AnalysisConsumer>();

        // ============= QUEUE PUBLISHER FOR ANALYSIS =============
        
        // ============= BET BLOCKER PIPELINE =============
        services.AddSingleton<IBetBlockerPipeline, BetBlockerPipeline>();
    }
}



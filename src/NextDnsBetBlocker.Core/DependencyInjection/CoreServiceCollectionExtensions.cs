namespace NextDnsBetBlocker.Core.DependencyInjection;

using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services;
using NextDnsBetBlocker.Core.Services.Import;
using NextDnsBetBlocker.Core.Services.Queue;
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
        services.AddSingleton(c => new TableServiceClient(c.GetRequiredService<IOptions<WorkerSettings>>().Value.AzureStorageConnectionString));
        services.AddSingleton(c => new BlobServiceClient(c.GetRequiredService<IOptions<WorkerSettings>>().Value.AzureStorageConnectionString));

        // ============= MEMORY CACHE =============
        services.AddMemoryCache();

        // ============= CHECKPOINT STORE (Shared) =============
        services.AddSingleton<ICheckpointStore, CheckpointStore>();


        // ============= STORAGE INFRASTRUCTURE INITIALIZER =============
        services.AddSingleton<IStorageInfrastructureInitializer>(sp =>
        {
            var tableRepo = sp.GetRequiredService<IListTableStorageRepository>();
            return new StorageInfrastructureInitializer(
                tableRepo,
                sp.GetRequiredService<IOptions<WorkerSettings>>().Value.AzureStorageConnectionString,
                sp.GetRequiredService<ILogger<StorageInfrastructureInitializer>>());
        });
    }

    /// <summary>
    /// Serviços específicos para a camada IMPORTER (local)
    /// </summary>
    private static void RegisterImporterServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // ============= IMPORT INFRASTRUCTURE =============
        services.AddSingleton<IImportMetricsCollector, ImportMetricsCollector>();

        services.AddSingleton<IImportRateLimiter>(sp => new ImportRateLimiter(150000));

        // ============= HTTP CLIENT =============
        services.AddHttpClient<IListImportProducer, ListImportProducer>();

        // ============= LIST IMPORT CONFIGS =============
        // Register all ListImportConfig instances from appsettings
        services.AddSingleton<IEnumerable<ListImportConfig>>(sp =>
        {
            var configs = new List<ListImportConfig>();

            var trancoConfig = configuration.GetSection("ListImport:TrancoList").Get<ListImportConfig>();
            if (trancoConfig != null)
            {
                configs.Add(trancoConfig);
            }

            var hageziConfig = configuration.GetSection("ListImport:Hagezi").Get<ListImportConfig>();
            if (hageziConfig != null)
            {
                configs.Add(hageziConfig);
            }

            return configs;
        });

        // ============= CHECKPOINT STORE =============
        // ICheckpointStore é registrado em RegisterSharedServices

        // ============= PARALLEL IMPORT CONFIG =============
        services.AddOptions<ParallelImportConfig>()
            .Bind(configuration.GetSection("ParallelImport"))
            .ValidateOnStart();

        services.AddSingleton<ParallelImportConfig>(sp =>
            sp.GetRequiredService<IOptionsSnapshot<ParallelImportConfig>>().Value);

        // ============= IMPORT CONSUMER & ORCHESTRATOR =============
        services.AddSingleton<IListImportConsumer, ListImportConsumer>();
        services.AddSingleton<IListImportOrchestrator, ListImportOrchestrator>();

        // ============= STORAGE REPOSITORIES =============
        services.AddSingleton<IListTableStorageRepository>(sp =>
        {
            var connString = sp.GetRequiredService<IOptions<ListImportConfig>>().Value.AzureStorageConnectionString;
            return new ListTableStorageRepository(
                connString,
                sp.GetRequiredService<ILogger<ListTableStorageRepository>>());
        });

        services.AddSingleton<IListBlobRepository>(sp =>
        {
            var connString = sp.GetRequiredService<IOptions<ListImportConfig>>().Value.AzureStorageConnectionString;
            return new ListBlobRepository(
                connString,
                "tranco-lists",
                sp.GetRequiredService<ILogger<ListBlobRepository>>());
        });

        // ============= LIST TABLE PROVIDER (with cache) =============
        RegisterListTableProvider(services, (sp) => sp.GetRequiredService<IOptions<ListImportConfig>>().Value.AzureStorageConnectionString);

        // ============= GENERIC LIST IMPORTER =============
        services.AddSingleton<GenericListImporter>(sp =>
        {
            return new GenericListImporter(
                sp.GetRequiredService<ILogger<GenericListImporter>>(),
                sp.GetRequiredService<IListImportOrchestrator>(),
                sp.GetRequiredService<IListBlobRepository>(),
                sp.GetRequiredService<IListTableStorageRepository>());
        });

        // ============= GENERIC LIST IMPORTER (as IListImporter) =============
        services.AddSingleton<IListImporter>(sp =>
            sp.GetRequiredService<GenericListImporter>());

        // ============= HAGEZI LIST IMPORTER (with IOptions) =============
        services.AddOptions<ListImportConfig>("Hagezi")
            .Bind(configuration.GetSection("ListImport:Hagezi"))
            .ValidateOnStart();

        // ============= HAGEZI PROVIDER CONFIG (with IOptions) =============
        services.AddOptions<HageziProviderConfig>()
            .Bind(configuration.GetSection("HaGeZi"))
            .ValidateOnStart();

        // Register HageziProvider for Importer layer
        services.AddSingleton<IHageziProvider>(sp =>
        {
            var connString = sp.GetRequiredService<IOptions<ListImportConfig>>().Value.AzureStorageConnectionString;
            var blobServiceClient = new BlobServiceClient(connString);
            var containerClient = blobServiceClient.GetBlobContainerClient("hagezi-lists");

            return new HageziProvider(
                containerClient,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<HageziProvider>>(),
                sp.GetRequiredService<IOptions<HageziProviderConfig>>());
        });


        // ============= TRANCO ALLOW LIST PROVIDER =============
        services.AddSingleton<ITrancoAllowlistProvider, TrancoAllowlistProvider>();
    }

    private static void RegisterListTableProvider(IServiceCollection services, Func<IServiceProvider, string> fnConnectionString)
    {
        services.AddSingleton<IPartitionKeyStrategy>(sp => new PartitionKeyStrategy(10));

        services.AddSingleton<IListTableProvider>(sp =>
        {            
            var tableServiceClient = new TableServiceClient(fnConnectionString.Invoke(sp));
            var tableClient = tableServiceClient.GetTableClient("TrancoList");
            var cache = sp.GetRequiredService<IMemoryCache>();
            var partitionStrategy = sp.GetRequiredService<IPartitionKeyStrategy>();
            return new ListTableProvider(
                tableClient,
                cache,
                partitionStrategy,
                sp.GetRequiredService<ILogger<ListTableProvider>>());
        });
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
        services.AddHttpClient<INextDnsClient, NextDnsClient>();
        services.AddHttpClient("HageziProvider")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        // ============= HAGEZI PROVIDER CONFIG (with IOptions) =============
        services.AddOptions<HageziProviderConfig>()
            .Bind(configuration.GetSection("HaGeZi"))
            .ValidateOnStart();


        RegisterListTableProvider(services, sp => sp.GetRequiredService<IOptions<WorkerSettings>>().Value.AzureStorageConnectionString);

        // ============= AZURE STORAGE - TABLE CLIENTS =============
        services.AddSingleton<IBlockedDomainStore, BlockedDomainStore>();

        // ICheckpointStore já é registrado em RegisterSharedServices
        services.AddSingleton<IGamblingSuspectStore, GamblingSuspectStore>();

        // ============= HAGEZI GAMBLING STORE (Table Storage Query) =============
        services.AddSingleton<IHageziGamblingStore, HageziGamblingStore>();


        // ============= BLOB STORAGE FOR HAGEZI =============        

        services.AddSingleton<IHageziProvider>(sp =>
            new HageziProvider(
                sp.GetRequiredService<BlobServiceClient>().GetBlobContainerClient("hagezi-gambling"),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<HageziProvider>>(),
                sp.GetRequiredService<IOptions<HageziProviderConfig>>()));  // ← Injetar IOptions

        // ============= CLASSIFIER =============
        services.AddSingleton<IBetClassifier, BetClassifier>();

        // ============= PIPELINE COMPONENTS =============
        services.AddSingleton<ILogsProducer, LogsProducer>();
        services.AddSingleton<IClassifierConsumer, ClassifierConsumer>();
        services.AddSingleton<ITrancoAllowlistConsumer, TrancoAllowlistConsumer>();
        services.AddSingleton<IAnalysisConsumer, AnalysisConsumer>();

        // ============= QUEUE PUBLISHER FOR ANALYSIS =============
        services.AddSingleton<ISuspectDomainQueuePublisher>(sp =>
        {
            var connString = sp.GetRequiredService<IOptions<WorkerSettings>>().Value.AzureStorageConnectionString;
            return new SuspectDomainQueuePublisher(
                connString,
                sp.GetRequiredService<ILogger<SuspectDomainQueuePublisher>>());
        });

        // ============= BET BLOCKER PIPELINE =============
        services.AddSingleton<IBetBlockerPipeline, BetBlockerPipeline>();

        // ============= GAMBLING SUSPECT ANALYZER =============
        services.AddSingleton<IGamblingSuspectAnalyzer, GamblingSuspectAnalyzer>();
    }
}



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
        var settings = configuration.Get<CoreSettings>() ?? new CoreSettings();

        // ============= SHARED SERVICES (ambas camadas) =============
        RegisterSharedServices(services, configuration, settings);

        // ============= LAYER-SPECIFIC SERVICES =============
        switch (layerType)
        {
            case ServiceLayerType.Importer:
                RegisterImporterServices(services, configuration, settings);
                break;

            case ServiceLayerType.Analysis:
                RegisterAnalysisServices(services, configuration, settings);
                break;

            default:
                throw new InvalidOperationException($"Unknown service layer type: {layerType}");
        }

        // ============= SETTINGS =============
        services.AddSingleton(settings);

        return services;
    }

    /// <summary>
    /// Serviços compartilhados por ambas camadas
    /// </summary>
    private static void RegisterSharedServices(
        IServiceCollection services,
        IConfiguration configuration,
        CoreSettings settings)
    {
        // ============= AZURE STORAGE =============
        if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
        {
            var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
            var blobServiceClient = new BlobServiceClient(settings.AzureStorageConnectionString);

            services.AddSingleton(tableServiceClient);
            services.AddSingleton(blobServiceClient);
        }

        // ============= MEMORY CACHE =============
        services.AddMemoryCache();

        // ============= STORAGE INFRASTRUCTURE INITIALIZER =============
        services.AddSingleton<IStorageInfrastructureInitializer>(sp =>
        {
            var tableRepo = sp.GetRequiredService<IListTableStorageRepository>();
            return new StorageInfrastructureInitializer(
                tableRepo,
                settings.AzureStorageConnectionString,
                sp.GetRequiredService<ILogger<StorageInfrastructureInitializer>>());
        });
    }

    /// <summary>
    /// Serviços específicos para a camada IMPORTER (local)
    /// </summary>
    private static void RegisterImporterServices(
        IServiceCollection services,
        IConfiguration configuration,
        CoreSettings settings)
    {
        // ============= IMPORT INFRASTRUCTURE =============
        services.AddSingleton<IImportMetricsCollector, ImportMetricsCollector>();
        services.AddSingleton<IPartitionKeyStrategy>(sp => new PartitionKeyStrategy(10));
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
        if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
        {
            services.AddSingleton<ICheckpointStore>(sp =>
            {
                var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
                var tableClient = tableServiceClient.GetTableClient("ImportCheckpoint");
                tableClient.CreateIfNotExists();
                return new CheckpointStore(
                    tableClient,
                    sp.GetRequiredService<ILogger<CheckpointStore>>());
            });
        }

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
            var connString = settings.AzureStorageConnectionString;
            return new ListTableStorageRepository(
                connString,
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
            var connString = settings.AzureStorageConnectionString;
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

    /// <summary>
    /// Serviços específicos para a camada ANALYSIS (cloud)
    /// </summary>
    private static void RegisterAnalysisServices(
        IServiceCollection services,
        IConfiguration configuration,
        CoreSettings settings)
    {
        // ============= HTTP CLIENTS =============
        services.AddHttpClient<INextDnsClient, NextDnsClient>();
        services.AddHttpClient("HageziProvider")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        // ============= NEXTDNS CLIENT CONFIG (with IOptions) =============
        services.AddOptions<NextDnsClientConfig>()
            .Bind(configuration.GetSection("NextDns"))
            .ValidateOnStart();

        // ============= HAGEZI PROVIDER CONFIG (with IOptions) =============
        services.AddOptions<HageziProviderConfig>()
            .Bind(configuration.GetSection("HaGeZi"))
            .ValidateOnStart();

        // ============= AZURE STORAGE - TABLE CLIENTS =============
        if (!string.IsNullOrEmpty(settings.AzureStorageConnectionString))
        {
            var tableServiceClient = new TableServiceClient(settings.AzureStorageConnectionString);
            var tableClient = tableServiceClient.GetTableClient("BlockedDomains");
            var checkpointTableClient = tableServiceClient.GetTableClient("AgentState");
            var suspectTableClient = tableServiceClient.GetTableClient("GamblingSuspects");

            tableClient.CreateIfNotExists();
            checkpointTableClient.CreateIfNotExists();
            suspectTableClient.CreateIfNotExists();

            services.AddSingleton(tableClient);
            services.AddSingleton(suspectTableClient);
            services.AddSingleton<IBlockedDomainStore>(sp =>
                new BlockedDomainStore(tableClient, sp.GetRequiredService<ILogger<BlockedDomainStore>>()));
            services.AddSingleton<ICheckpointStore>(sp =>
                new CheckpointStore(checkpointTableClient, sp.GetRequiredService<ILogger<CheckpointStore>>()));
            services.AddSingleton<IGamblingSuspectStore>(sp =>
                new GamblingSuspectStore(suspectTableClient, sp.GetRequiredService<ILogger<GamblingSuspectStore>>()));
        }
        else
        {
            // Local file storage only if Worker is being used locally
            // Note: LocalBlockedDomainStore and LocalCheckpointStore are in Worker project
            // They should be registered in Worker's Program.cs for local development
        }

        // ============= BLOB STORAGE FOR HAGEZI =============
        BlobContainerClient containerClient;
        if (settings.UseBlobStorage && !string.IsNullOrEmpty(settings.AzureStorageConnectionString))
        {
            var blobServiceClient = new BlobServiceClient(settings.AzureStorageConnectionString);
            containerClient = blobServiceClient.GetBlobContainerClient("hagezi-gambling");

            services.AddSingleton(containerClient);
            services.AddSingleton<IHageziProvider>(sp =>
                new HageziProvider(
                    containerClient,
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<HageziProvider>>(),
                    sp.GetRequiredService<IOptions<HageziProviderConfig>>()));  // ← Injetar IOptions
        }
        else
        {
            // Use local filesystem for development
            // Note: LocalBlobContainerClient is in Worker project
            // For development, it should be registered in Worker's Program.cs
            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "data");
            Directory.CreateDirectory(localPath);
            containerClient = null; // Will be set by Worker if needed
        }

        // ============= ALLOWLIST PROVIDER =============
        var allowlistPath = Path.Combine(Directory.GetCurrentDirectory(), "allowlist.txt");
        services.AddSingleton<IAllowlistProvider>(sp =>
            new AllowlistProvider(allowlistPath, sp.GetRequiredService<ILogger<AllowlistProvider>>()));

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
            var connString = settings.AzureStorageConnectionString;
            return new SuspectDomainQueuePublisher(
                connString,
                sp.GetRequiredService<ILogger<SuspectDomainQueuePublisher>>());
        });

        // ============= BET BLOCKER PIPELINE =============
        services.AddSingleton<IBetBlockerPipeline>(sp =>
            new BetBlockerPipeline(
                sp.GetRequiredService<INextDnsClient>(),
                sp.GetRequiredService<ICheckpointStore>(),
                sp.GetRequiredService<IBlockedDomainStore>(),
                sp.GetRequiredService<IHageziProvider>(),
                sp.GetRequiredService<IBetClassifier>(),
                sp.GetRequiredService<ILogger<BetBlockerPipeline>>(),
                sp.GetRequiredService<ILogsProducer>(),
                sp.GetRequiredService<IClassifierConsumer>(),
                sp.GetRequiredService<ITrancoAllowlistConsumer>(),
                sp.GetRequiredService<IAnalysisConsumer>(),
                settings.RateLimitPerSecond));

        // ============= GAMBLING SUSPECT ANALYZER =============
        services.AddSingleton<IGamblingSuspectAnalyzer, GamblingSuspectAnalyzer>();
    }
}

/// <summary>
/// Core settings compartilhadas entre Importer e Analysis
/// </summary>
public class CoreSettings
{
    public string AzureStorageConnectionString { get; set; } = string.Empty;
    public bool UseBlobStorage { get; set; } = true;
    public int RateLimitPerSecond { get; set; } = 1000;
}

namespace NextDnsBetBlocker.Core.Services.Storage;

using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Inicializador centralizado para infraestrutura de armazenamento
/// Gerencia criação de tabelas e containers
/// Centraliza toda a lógica que estava distribuída em Program.cs
/// </summary>
public class StorageInfrastructureInitializer : IStorageInfrastructureInitializer
{
    private readonly IListTableStorageRepository _tableRepository;
    private readonly string? _azureStorageConnectionString;
    private readonly ILogger<StorageInfrastructureInitializer> _logger;

    // Tabelas a serem criadas
    private static readonly string[] TablesRequired = new[]
    {
        "AgentState",           // Checkpoint tracking
        "BlockedDomains",       // Domínios bloqueados
        "GamblingSuspects",     // Suspeitos de jogo
        "TrancoList"            // Lista Tranco confiável
    };

    // Containers Blob a serem criados
    private static readonly string[] ContainersRequired = new[]
    {
        "hagezi-gambling",      // HaGeZi gambling list
        "tranco-lists"          // Tranco list files
    };

    public StorageInfrastructureInitializer(
        IListTableStorageRepository tableRepository,
        string? azureStorageConnectionString,
        ILogger<StorageInfrastructureInitializer> logger)
    {
        _tableRepository = tableRepository;
        _azureStorageConnectionString = azureStorageConnectionString;
        _logger = logger;
    }

    /// <summary>
    /// Inicializa toda a infraestrutura (tabelas + containers)
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting storage infrastructure initialization");

        try
        {
            // Inicializar tabelas
            await InitializeTablesAsync(cancellationToken);

            // Inicializar containers
            await InitializeContainersAsync(cancellationToken);

            _logger.LogInformation("Storage infrastructure initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize storage infrastructure");
            throw;
        }
    }

    /// <summary>
    /// Inicializa todas as tabelas necessárias
    /// </summary>
    public async Task InitializeTablesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing {Count} tables", TablesRequired.Length);

        foreach (var tableName in TablesRequired)
        {
            await InitializeTableAsync(tableName, cancellationToken);
        }

        _logger.LogInformation("All {Count} tables initialized", TablesRequired.Length);
    }

    /// <summary>
    /// Inicializa uma tabela específica
    /// </summary>
    public async Task InitializeTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Ensuring table exists: {TableName}", tableName);

            await _tableRepository.EnsureTableExistsAsync(tableName, cancellationToken);

            _logger.LogInformation("✓ Table initialized: {TableName}", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to initialize table: {TableName}", tableName);
            throw;
        }
    }

    /// <summary>
    /// Inicializa todos os containers necessários
    /// </summary>
    public async Task InitializeContainersAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_azureStorageConnectionString))
        {
            _logger.LogWarning("AzureStorageConnectionString not configured - skipping container initialization");
            return;
        }

        _logger.LogInformation("Initializing {Count} containers", ContainersRequired.Length);

        try
        {
            var blobServiceClient = new BlobServiceClient(_azureStorageConnectionString);

            foreach (var containerName in ContainersRequired)
            {
                await InitializeContainerAsync(containerName, blobServiceClient, cancellationToken);
            }

            _logger.LogInformation("All {Count} containers initialized", ContainersRequired.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize containers");
            // Não lançar erro - containers são opcionais
        }
    }

    /// <summary>
    /// Inicializa um container específico
    /// </summary>
    public async Task InitializeContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_azureStorageConnectionString))
        {
            _logger.LogWarning("AzureStorageConnectionString not configured - skipping container: {ContainerName}", containerName);
            return;
        }

        try
        {
            var blobServiceClient = new BlobServiceClient(_azureStorageConnectionString);
            await InitializeContainerAsync(containerName, blobServiceClient, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize container: {ContainerName}", containerName);
        }
    }

    /// <summary>
    /// Inicializa um container usando um BlobServiceClient
    /// </summary>
    private async Task InitializeContainerAsync(
        string containerName,
        BlobServiceClient blobServiceClient,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Ensuring container exists: {ContainerName}", containerName);

            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("✓ Container initialized: {ContainerName}", containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to initialize container: {ContainerName}", containerName);
            // Não lançar erro - containers são opcionais
        }
    }

    /// <summary>
    /// Retorna lista de tabelas configuradas
    /// </summary>
    public IReadOnlyList<string> GetConfiguredTables() => TablesRequired;

    /// <summary>
    /// Retorna lista de containers configurados
    /// </summary>
    public IReadOnlyList<string> GetConfiguredContainers() => ContainersRequired;
}

namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Importador específico para Tranco List
/// Configuração lida do appsettings.json
/// </summary>
public class TrancoListImporter
{
    private readonly IListImporter _genericImporter;
    private readonly ILogger<TrancoListImporter> _logger;
    private readonly ListImportConfig _config;

    public TrancoListImporter(
        IListImporter genericImporter,
        ILogger<TrancoListImporter> logger,
        IConfiguration configuration)
    {
        _genericImporter = genericImporter;
        _logger = logger;

        // ✅ Ler configuração do appsettings.json
        var trancoSection = configuration.GetSection("ListImport:TrancoList");

        // Usar valores do config, com fallbacks para defaults
        _config = new ListImportConfig
        {
            ListName = trancoSection.GetValue<string>("ListName") ?? "TrancoList",
            SourceUrl = trancoSection.GetValue<string>("SourceUrl") ?? "https://tranco-list.eu/top-1m.csv.zip",
            TableName = trancoSection.GetValue<string>("TableName") ?? "TrancoList",
            BlobContainer = trancoSection.GetValue<string>("BlobContainer") ?? "tranco-lists",
            BatchSize = trancoSection.GetValue<int>("BatchSize", 100),
            MaxPartitions = trancoSection.GetValue<int>("MaxPartitions", 10),
            ThrottleOperationsPerSecond = trancoSection.GetValue<int>("ThrottleOperationsPerSecond", 150000),
            ChannelCapacity = trancoSection.GetValue<int>("ChannelCapacity", 10000)
        };

        _logger.LogInformation(
            "TrancoListImporter configured: URL={Url}, BatchSize={BatchSize}, Partitions={Partitions}",
            _config.SourceUrl,
            _config.BatchSize,
            _config.MaxPartitions);
    }

    /// <summary>
    /// Executa importação completa da lista Tranco
    /// </summary>
    public async Task<ImportMetrics> ImportAsync(
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Tranco List import");
        return await _genericImporter.ImportAsync(_config, progress, cancellationToken);
    }

    /// <summary>
    /// Executa diff da lista Tranco
    /// Mantém apenas diferenças desde última importação
    /// </summary>
    public async Task<ImportMetrics> ImportDiffAsync(
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Tranco List diff import");
        return await _genericImporter.ImportDiffAsync(_config, progress, cancellationToken);
    }

    /// <summary>
    /// Factory method para criar config (compatibilidade)
    /// Nota: Preferir injetar TrancoListImporter ao invés de usar este método
    /// </summary>
    public static ListImportConfig CreateConfig(
        string? sourceUrl = null,
        int? batchSize = null,
        int? maxPartitions = null,
        int? throttleOpsPerSec = null)
    {
        return new ListImportConfig
        {
            ListName = "TrancoList",
            SourceUrl = sourceUrl ?? "https://tranco-list.eu/top-1m.csv.zip",
            TableName = "TrancoList",
            BlobContainer = "tranco-lists",
            BatchSize = batchSize ?? 100,
            MaxPartitions = maxPartitions ?? 10,
            ThrottleOperationsPerSecond = throttleOpsPerSec ?? 150000,
            ChannelCapacity = 10000
        };
    }
}

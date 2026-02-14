namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Importador específico para Tranco List
/// Configuração lida do appsettings.json via IOptions
/// </summary>
public class TrancoListImporter
{
    private readonly IListImporter _genericImporter;
    private readonly ILogger<TrancoListImporter> _logger;
    private readonly ListImportConfig _config;

    public TrancoListImporter(
        IListImporter genericImporter,
        ILogger<TrancoListImporter> logger,
        IOptions<ListImportConfig> options)
    {
        _genericImporter = genericImporter;
        _logger = logger;

        // ✅ Injetar via IOptions (strongly typed)
        _config = options.Value;

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
    /// Factory method para criar config (OBSOLETO)
    /// Use IOptions<ListImportConfig> injetado via DI ao invés disso
    /// Mantido apenas para compatibilidade retroativa
    /// </summary>
    [Obsolete("Use IOptions<ListImportConfig> dependency injection instead")]
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

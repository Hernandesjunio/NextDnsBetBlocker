namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Importador específico para Tranco List
/// Configuração e customizações para fonte Tranco
/// </summary>
public class TrancoListImporter
{
    private readonly IListImporter _genericImporter;
    private readonly ILogger<TrancoListImporter> _logger;
    private readonly ListImportConfig _config;

    public TrancoListImporter(
        IListImporter genericImporter,
        ILogger<TrancoListImporter> logger)
    {
        _genericImporter = genericImporter;
        _logger = logger;
        
        // Configuração padrão para Tranco
        _config = new ListImportConfig
        {
            ListName = "TrancoList",
            SourceUrl = "https://tranco-list.eu/top-1m.csv.zip",
            TableName = "TrancoList",
            BlobContainer = "tranco-lists",
            BatchSize = 100,
            MaxPartitions = 10,
            ThrottleOperationsPerSecond = 150000,
            ChannelCapacity = 10000
        };
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
    /// Configuração customizada para Tranco
    /// Permite override de parâmetros
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

namespace NextDnsBetBlocker.Core.Services;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using NextDnsBetBlocker.Core.Services.Import;

/// <summary>
/// Provider para Tranco List (1M domínios confiáveis)
/// REFATORADO: Usa Table Storage ao invés de HashSet em memória
/// Queries eficientes com cache 5 minutos
/// </summary>
public class TrancoAllowlistProvider : ITrancoAllowlistProvider
{
    private readonly IListTableProvider _tableProvider;
    private readonly IListImporter _listImporter;
    private readonly ILogger<TrancoAllowlistProvider> _logger;

    private const string TrancoTableName = "TrancoList";

    public TrancoAllowlistProvider(
        IListTableProvider tableProvider,
        IListImporter listImporter,
        ILogger<TrancoAllowlistProvider> logger)
    {
        _tableProvider = tableProvider;
        _listImporter = listImporter;
        _logger = logger;
    }
       
    /// <summary>
    /// Verifica se domínio existe na Tranco List (Table Storage)
    /// Query eficiente com cache 5 minutos
    /// </summary>
    public async Task<bool> DomainExistsAsync(string domain, CancellationToken cancellationToken = default)
    {
        return await _tableProvider.DomainExistsAsync(
            TrancoTableName,
            domain,
            cancellationToken);
    }

    /// <summary>
    /// Força refresh da Tranco List
    /// Baixa arquivo novo e faz diff com anterior
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Force refreshing Tranco List");
        
        try
        {
            var config = TrancoListImporter.CreateConfig();
            var progress = new Progress<ImportProgress>();
            
            // Executar diff import (mais eficiente que full import)
            var metrics = await _listImporter.ImportDiffAsync(config, progress, cancellationToken);
            
            _logger.LogInformation(
                "Tranco List refreshed: +{Added}, -{Removed}, Errors={Errors}",
                metrics.TotalInserted,
                metrics.TotalErrors,
                metrics.TotalErrors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh Tranco List");
            throw;
        }
    }

    /// <summary>
    /// Retorna contagem total de domínios na Tranco List
    /// </summary>
    public async Task<long> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        return await _tableProvider.CountAsync(TrancoTableName, cancellationToken);
    }
}

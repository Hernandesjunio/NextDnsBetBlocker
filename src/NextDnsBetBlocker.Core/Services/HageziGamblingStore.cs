namespace NextDnsBetBlocker.Core.Services;

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Store para querying domínios gambling da tabela HageziGambling
/// Implementação similar a GamblingSuspectStore mas para leitura apenas
/// </summary>
public class HageziGamblingStore : IHageziGamblingStore
{
    private readonly TableClient _tableClient;
    private readonly ILogger<HageziGamblingStore> _logger;
    private const string PartitionKey = "gambling";

    public HageziGamblingStore(TableClient tableClient, ILogger<HageziGamblingStore> logger)
    {
        _tableClient = tableClient;
        _logger = logger;
    }

    /// <summary>
    /// Verifica se domínio está na lista de gambling do HaGeZi
    /// Usa GetEntityAsync para query eficiente (ponto exato)
    /// </summary>
    public async Task<bool> IsGamblingDomainAsync(string domain)
    {
        try
        {
            var normalizedDomain = domain.ToLowerInvariant().Trim();

            var response = await _tableClient.GetEntityAsync<TableEntity>(
                PartitionKey,
                normalizedDomain);

            return response.Value != null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Domínio não encontrado na lista
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if domain {Domain} is in HaGeZi gambling list", domain);
            return false;
        }
    }

    /// <summary>
    /// Retorna total de domínios gambling armazenados
    /// </summary>
    public async Task<int> GetTotalCountAsync()
    {
        try
        {
            var query = _tableClient.QueryAsync<TableEntity>(
                x => x.PartitionKey == PartitionKey);

            int count = 0;
            await foreach (var entity in query)
            {
                count++;
            }

            _logger.LogInformation("HaGeZi gambling list contains {Count} domains", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting HaGeZi gambling domains");
            return 0;
        }
    }
}

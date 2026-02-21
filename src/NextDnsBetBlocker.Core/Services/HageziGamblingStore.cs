namespace NextDnsBetBlocker.Core.Services;

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Store para querying domínios gambling da tabela HageziGambling
/// Estratégia: exato + wildcards em paralelo (conservador)
/// Exemplo: test.1021.casino → testa exato e *.1021.casino
/// </summary>
public class HageziGamblingStore : IHageziGamblingStore
{
    private readonly TableClient _tableClient;
    private readonly ILogger<HageziGamblingStore> _logger;
    private const string PartitionKey = "gambling";

    public HageziGamblingStore(TableServiceClient tableServiceClient, ILogger<HageziGamblingStore> logger)
    {
        _tableClient = tableServiceClient.GetTableClient("HageziGambling");
        _tableClient.CreateIfNotExists();
        _logger = logger;
    }

    /// <summary>
    /// Verifica se domínio está na lista de gambling do HaGeZi
    /// 1. Testa exato (rápido)
    /// 2. Se não encontrar, testa wildcards em paralelo (*.xxx.casino)
    /// Retorna true se qualquer um dos testes retornar true
    /// </summary>
    public async Task<bool> IsGamblingDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var normalizedDomain = domain.ToLowerInvariant().Trim();

        // 1. Teste exato primeiro (rápido)
        if (await CheckExactDomainAsync(normalizedDomain, cancellationToken))
        {
            _logger.LogDebug("Domain {Domain} found as exact match", normalizedDomain);
            return true;
        }

        // 2. Teste wildcards em paralelo (conservador: apenas últimos 2 componentes)
        var isWildcardMatch = await CheckWildcardsAsync(normalizedDomain, cancellationToken);

        if (isWildcardMatch)
        {
            _logger.LogInformation("Domain {Domain} matched wildcard pattern", normalizedDomain);
        }

        return isWildcardMatch;
    }

    /// <summary>
    /// Verifica domínio exato na tabela
    /// </summary>
    private async Task<bool> CheckExactDomainAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                PartitionKey,
                domain,
                cancellationToken: cancellationToken);

            return response.Value != null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking exact domain {Domain}", domain);
            return false;
        }
    }

    /// <summary>
    /// Verifica wildcards em paralelo usando Task.WaitAny
    /// Retorna assim que encontrar uma correspondência
    /// Estratégia conservadora: apenas *.xxx.casino (últimos 2 componentes)
    /// </summary>
    private async Task<bool> CheckWildcardsAsync(string domain, CancellationToken cancellationToken)
    {
        var wildcards = GenerateConservativeWildcards(domain);

        if (wildcards.Count == 0)
        {
            return false;
        }

        // Criar tasks para verificar cada wildcard
        var tasks = wildcards
            .Select(wc => CheckWildcardAsync(wc, cancellationToken))
            .ToArray();

        if (tasks.Length == 0)
        {
            return false;
        }

        try
        {
            // WaitAny retorna assim que UMA task completar
            var completedTask = await Task.WhenAny(tasks);

            // Se a tarefa completada é Task<bool> e retornou true
            if (completedTask is Task<bool> boolTask && boolTask.Result)
            {
                return true;
            }

            // Se nenhuma retornou true, aguardar todas as outras completarem
            // (em caso de timeouts ou outros erros)
            await Task.WhenAll(tasks);

            // Se chegou aqui, todas retornaram false
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Wildcard check cancelled for domain {Domain}", domain);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during wildcard check for domain {Domain}", domain);
            return false;
        }
    }

    /// <summary>
    /// Gera wildcards conservadores (apenas últimos 2 componentes)
    /// Exemplo: test.1021.casino → *.1021.casino
    /// </summary>
    private static List<string> GenerateConservativeWildcards(string domain)
    {
        var parts = domain.Split('.');
        var wildcards = new List<string>();

        // Apenas últimos 2 componentes → *.xxx.casino
        // Isso evita bloquear tudo (ex: *.casino)
        if (parts.Length >= 2)
        {
            var twoLevelWildcard = $"*.{string.Join(".", parts.TakeLast(2))}";
            wildcards.Add(twoLevelWildcard);
        }

        return wildcards;
    }

    /// <summary>
    /// Verifica um wildcard específico
    /// </summary>
    private async Task<bool> CheckWildcardAsync(string wildcard, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                PartitionKey,
                wildcard,
                cancellationToken: cancellationToken);

            return response.Value != null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Wildcard não encontrado
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking wildcard {Wildcard}", wildcard);
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

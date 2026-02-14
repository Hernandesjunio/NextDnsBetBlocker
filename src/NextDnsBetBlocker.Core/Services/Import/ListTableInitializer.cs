namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Inicializador de tabelas para listas de domínios
/// Garante que as tabelas existem no Table Storage
/// </summary>
public class ListTableInitializer
{
    private readonly IListTableStorageRepository _tableRepository;
    private readonly ILogger<ListTableInitializer> _logger;

    public ListTableInitializer(
        IListTableStorageRepository tableRepository,
        ILogger<ListTableInitializer> logger)
    {
        _tableRepository = tableRepository;
        _logger = logger;
    }

    /// <summary>
    /// Inicializa todas as tabelas de listas necessárias
    /// Cria se não existirem
    /// </summary>
    public async Task InitializeAllTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = new[]
        {
            "TrancoList",
            // Adicionar outras tabelas conforme necessário
            // "HageziList",
            // "PiHoleList"
        };

        foreach (var tableName in tables)
        {
            await InitializeTableAsync(tableName, cancellationToken);
        }
    }

    /// <summary>
    /// Inicializa uma tabela específica
    /// </summary>
    public async Task InitializeTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Ensuring list table exists: {TableName}", tableName);
            
            await _tableRepository.EnsureTableExistsAsync(tableName, cancellationToken);
            
            _logger.LogInformation("List table initialized successfully: {TableName}", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize list table: {TableName}", tableName);
            throw;
        }
    }
}

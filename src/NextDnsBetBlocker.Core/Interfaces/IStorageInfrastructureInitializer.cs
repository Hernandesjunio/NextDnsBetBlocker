namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Inicializador centralizado para toda infraestrutura de armazenamento
/// Gerencia tabelas (Table Storage) e containers (Blob Storage)
/// Idempotente: safe to call multiple times
/// </summary>
[Obsolete("This interface is deprecated. Storage infrastructure is now initialized automatically by the respective services. Manual initialization is no longer necessary.", true)]
public interface IStorageInfrastructureInitializer
{
    /// <summary>
    /// Inicializa toda a infraestrutura de armazenamento
    /// - Tabelas: AgentState, BlockedDomains, GamblingSuspects, TrancoList
    /// - Containers: hagezi-gambling, tranco-lists
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicializa apenas tabelas
    /// </summary>
    Task InitializeTablesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicializa apenas containers (Blob)
    /// </summary>
    Task InitializeContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicializa uma tabela específica
    /// </summary>
    Task InitializeTableAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicializa um container específico
    /// </summary>
    Task InitializeContainerAsync(string containerName, CancellationToken cancellationToken = default);
}

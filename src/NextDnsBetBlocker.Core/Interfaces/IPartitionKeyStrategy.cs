namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Estratégia para gerar partition key baseado em domínio
/// Usado para sharding em Table Storage
/// </summary>
public interface IPartitionKeyStrategy
{
    /// <summary>
    /// Gera a partition key para um domínio
    /// Deve ser determinístico: mesmo domínio → sempre mesma partição
    /// </summary>
    string GetPartitionKey(string domain);

    /// <summary>
    /// Retorna o número de partições usadas
    /// </summary>
    int GetPartitionCount();
}

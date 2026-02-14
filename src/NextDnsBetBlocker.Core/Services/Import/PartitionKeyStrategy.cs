namespace NextDnsBetBlocker.Core.Services.Import;

using NextDnsBetBlocker.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Estratégia de particionamento usando hash xxHash-like
/// Garante distribuição uniforme de domínios entre partições
/// Determinístico: mesmo domínio → sempre mesma partição
/// </summary>
public class PartitionKeyStrategy : IPartitionKeyStrategy
{
    private readonly int _partitionCount;

    public PartitionKeyStrategy(int partitionCount)
    {
        if (partitionCount < 1)
            throw new ArgumentException("Partition count must be at least 1", nameof(partitionCount));

        _partitionCount = partitionCount;
    }

    /// <summary>
    /// Gera partition key usando hash do domínio
    /// Distribui uniformemente entre 0 e partitionCount-1
    /// </summary>
    public string GetPartitionKey(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain cannot be empty", nameof(domain));

        // Usar SHA256 para hash consistente
        var hash = ComputeSha256Hash(domain.ToLowerInvariant());
        
        // Converter primeiros 8 bytes do hash para número
        var hashValue = BitConverter.ToUInt64(hash, 0);
        
        // Mapear para partição
        var partitionNumber = (int)(hashValue % (ulong)_partitionCount);
        
        // Formato: "partition_0", "partition_1", etc
        return $"partition_{partitionNumber:D2}";
    }

    public int GetPartitionCount() => _partitionCount;

    private static byte[] ComputeSha256Hash(string input)
    {
        using (var sha256 = SHA256.Create())
        {
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        }
    }
}

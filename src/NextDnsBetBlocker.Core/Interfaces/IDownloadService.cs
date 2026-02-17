namespace NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Interface para abstração do download e parse de domínios.
/// Permite mockar em testes sem fazer requisições HTTP reais.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// Baixar e fazer parse de domínios de múltiplas fontes.
    /// </summary>
    /// <param name="sourceUrls">URLs das fontes (HTTP/HTTPS)</param>
    /// <param name="cancellationToken">Token de cancelamento</param>
    /// <returns>HashSet com domínios únicos em lowercase</returns>
    Task<HashSet<string>> DownloadAndParseAsync(
        string[] sourceUrls,
        CancellationToken cancellationToken);
}

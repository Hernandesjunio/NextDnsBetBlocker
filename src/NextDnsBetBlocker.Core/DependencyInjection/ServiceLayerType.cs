namespace NextDnsBetBlocker.Core.DependencyInjection;

/// <summary>
/// Tipo de camada de serviço para configurar DI apropriadamente
/// </summary>
public enum ServiceLayerType
{
    /// <summary>
    /// Local Importer: registra ImportListBackgroundService, etc
    /// </summary>
    Importer,

    /// <summary>
    /// Cloud Analysis: registra BetBlockerPipeline, ClassifierConsumer, etc
    /// </summary>
    Analysis
}

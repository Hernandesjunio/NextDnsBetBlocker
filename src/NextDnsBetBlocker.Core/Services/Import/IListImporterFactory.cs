namespace NextDnsBetBlocker.Core.Services.Import;

using NextDnsBetBlocker.Core.Interfaces;

/// <summary>
/// Factory para resolver o importer correto baseado no nome da lista
/// </summary>
public interface IListImporterFactory
{
    IListImporter? CreateImporter(string listName);
}

/// <summary>
/// Implementação padrão da factory
/// Resolve GenericListImporter, HageziListImporter, etc
/// </summary>
public class ListImporterFactory : IListImporterFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ListImporterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Criar importer específico para a lista
    /// Suporta: Hagezi → HageziListImporter, TrancoList → GenericListImporter
    /// </summary>
    public IListImporter? CreateImporter(string listName)
    {
        return listName.ToLowerInvariant() switch
        {
            "hagezi" or "hagazigambling" => 
                _serviceProvider.GetService(typeof(HageziListImporter)) as IListImporter,
            
            "trancolist" or "tranco" => 
                _serviceProvider.GetService(typeof(GenericListImporter)) as IListImporter,
            
            _ => null
        };
    }
}

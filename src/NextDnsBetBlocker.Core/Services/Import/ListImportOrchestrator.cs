namespace NextDnsBetBlocker.Core.Services.Import;

using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Threading.Channels;

/// <summary>
/// Orquestrador da importação
/// Coordena produtor, consumidor e coleta de métricas
/// Executa o pipeline completo de importação
/// </summary>
public class ListImportOrchestrator : IListImportOrchestrator
{
    private readonly ILogger<ListImportOrchestrator> _logger;
    private readonly IListImportProducer _producer;
    private readonly IListImportConsumer _consumer;
    private readonly IImportMetricsCollector _metricsCollector;
    private readonly IPartitionKeyStrategy _partitionKeyStrategy;

    public ListImportOrchestrator(
        ILogger<ListImportOrchestrator> logger,
        IListImportProducer producer,
        IListImportConsumer consumer,
        IImportMetricsCollector metricsCollector,
        IPartitionKeyStrategy partitionKeyStrategy)
    {
        _logger = logger;
        _producer = producer;
        _consumer = consumer;
        _metricsCollector = metricsCollector;
        _partitionKeyStrategy = partitionKeyStrategy;
    }

    /// <summary>
    /// Executa a importação completa
    /// Coordena produtor e consumidor em paralelo usando channel
    /// </summary>
    public async Task<ImportMetrics> ExecuteImportAsync(
        ListImportItemConfig config,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting import orchestration for {ListName} into {TableName}",
                config.ListName,
                config.TableName);

            _metricsCollector.Reset();

            // Criar channel com bounded capacity para backpressure
            var channel = Channel.CreateBounded<string>(
                new BoundedChannelOptions(config.ChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

            // Executar produtor e consumidor em paralelo
            var producerTask = _producer.ProduceAsync(channel, config, cancellationToken);
            var consumerTask = _consumer.ConsumeAsync(channel, config, progress, cancellationToken);

            // Aguardar ambos completarem
            await Task.WhenAll(producerTask, consumerTask);

            var finalMetrics = _metricsCollector.GetCurrentMetrics();

            _logger.LogInformation(
                "Import orchestration completed for {ListName}: Total={Processed}, Inserted={Inserted}, Errors={Errors}, Duration={Duration}",
                config.ListName,
                finalMetrics.TotalProcessed,
                finalMetrics.TotalInserted,
                finalMetrics.TotalErrors,
                finalMetrics.ElapsedTime);

            return finalMetrics;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Import orchestration cancelled for {ListName}", config.ListName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import orchestration failed for {ListName}", config.ListName);
            throw;
        }
    }
}

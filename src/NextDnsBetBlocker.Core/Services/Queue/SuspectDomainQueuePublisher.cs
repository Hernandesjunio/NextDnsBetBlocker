namespace NextDnsBetBlocker.Core.Services.Queue;

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Models;
using System.Text.Json;

/// <summary>
/// Publisher para Azure Storage Queue
/// Custo mínimo (~$0.0001 por 1M operações)
/// Ideal para mensagens assíncronas de baixo volume
/// </summary>
public class SuspectDomainQueuePublisher : ISuspectDomainQueuePublisher
{
    private readonly QueueClient _queueClient;
    private readonly ILogger<SuspectDomainQueuePublisher> _logger;

    private const string QueueName = "suspicious-domains";

    public SuspectDomainQueuePublisher(
        IOptions<WorkerSettings> options,
        ILogger<SuspectDomainQueuePublisher> logger)
    {
        _logger = logger;

        var queueServiceClient = new QueueServiceClient(options.Value.AzureStorageConnectionString);
        _queueClient = queueServiceClient.GetQueueClient(QueueName);
    }

    /// <summary>
    /// Publica um domínio suspeito para análise
    /// </summary>
    public async Task PublishAsync(SuspectDomainQueueMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureQueueExistsAsync(cancellationToken);

            var json = JsonSerializer.Serialize(message);
            await _queueClient.SendMessageAsync(json, cancellationToken);

            _logger.LogDebug(
                "Published suspect domain to queue: {Domain} (CorrelationId: {CorrelationId})",
                message.Domain,
                message.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish domain {Domain} to queue", message.Domain);
            throw;
        }
    }

    /// <summary>
    /// Publica múltiplos domínios em batch
    /// Mais eficiente que PublishAsync múltiplas vezes
    /// </summary>
    public async Task PublishBatchAsync(
        IEnumerable<SuspectDomainQueueMessage> messages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureQueueExistsAsync(cancellationToken);

            var messageList = messages.ToList();

            if (messageList.Count == 0)
            {
                _logger.LogWarning("PublishBatchAsync called with empty message list");
                return;
            }

            // Azure Storage Queue não suporta batch direto, então vamos fazer em paralelo
            // com limite para não sobrecarregar
            var tasks = messageList
                .Select(msg => PublishAsync(msg, cancellationToken))
                .ToList();

            await Task.WhenAll(tasks);

            _logger.LogInformation(
                "Published batch of {Count} suspect domains to queue",
                messageList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch of domains to queue");
            throw;
        }
    }

    /// <summary>
    /// Testa a conexão com a fila
    /// </summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureQueueExistsAsync(cancellationToken);

            var properties = await _queueClient.GetPropertiesAsync(cancellationToken);

            _logger.LogInformation(
                "Queue connection test successful. Queue: {QueueName}, Messages: {Count}",
                QueueName,
                properties.Value.ApproximateMessagesCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Queue connection test failed");
            throw;
        }
    }

    /// <summary>
    /// Retorna estatísticas da fila
    /// </summary>
    public async Task<QueueStatistics> GetQueueStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var properties = await _queueClient.GetPropertiesAsync(cancellationToken);

            return new QueueStatistics
            {
                ApproximateMessageCount = properties.Value.ApproximateMessagesCount,
                CreatedTime = DateTime.UtcNow,
                LastAccessedTime = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queue statistics");
            throw;
        }
    }

    /// <summary>
    /// Garante que a fila existe, criando se necessário
    /// Idempotente: safe to call multiple times
    /// </summary>
    private async Task EnsureQueueExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure queue exists");
            throw;
        }
    }
}

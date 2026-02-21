namespace NextDnsBetBlocker.Core.Services.Import;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextDnsBetBlocker.Core.Models;

/// <summary>
/// Gerenciador de batches paralelos com isolamento por partição
/// 
/// Arquitetura:
/// - 1 Channel bounded por partição (producer/consumer independente)
/// - 1 consumer task dedicada por partição (processamento com pipelining)
/// - SemaphoreSlim por partição (pipeline depth configurável)
/// - SemaphoreSlim global (total de requests HTTP simultâneos)
/// - Rate limiter por partição (2k ops/s) + global (20k ops/s)
/// - Backoff exponencial POR PARTIÇÃO em caso de timeout/throttle
/// 
/// Se partition_07 sofre timeout, apenas ela aplica backoff.
/// As demais partições continuam processando normalmente.
/// </summary>
public sealed class ParallelBatchManager : IAsyncDisposable, IDisposable
{
    private readonly ParallelImportConfig _config;
    private readonly ILogger<ParallelBatchManager> _logger;
    private readonly ConcurrentDictionary<string, PartitionConsumer> _partitionConsumers;
    private readonly SemaphoreSlim _globalSemaphore;
    private readonly SlidingWindowRateLimiter _globalRateLimiter;
    private readonly ParallelBatchManagerMetrics _metrics;
    private readonly ConcurrentBag<Task> _consumerTasks;
    private volatile int _totalBatchesProcessed;    
    private volatile bool _disposed;

    // Producer staging: acumula items até formar batches de 100
    private readonly ConcurrentDictionary<string, List<DomainListEntry>> _currentBatches;
    private readonly ConcurrentDictionary<string, int> _partitionItemCounts;

    /// <summary>
    /// Consumer independente por partição com Channel, rate limiter e backoff próprios
    /// </summary>
    private sealed class PartitionConsumer : IAsyncDisposable
    {
        public Channel<BatchEnvelope> BatchChannel { get; }
        public SemaphoreSlim ConcurrencySemaphore { get; }
        public SlidingWindowRateLimiter RateLimiter { get; }
        public string PartitionKey { get; }
        public int CurrentBackoffMs { get; set; }

        public PartitionConsumer(string partitionKey, int channelCapacity, int maxConcurrency, int maxOpsPerSecond)
        {
            PartitionKey = partitionKey;
            BatchChannel = Channel.CreateBounded<BatchEnvelope>(new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });
            ConcurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            RateLimiter = new SlidingWindowRateLimiter(maxOpsPerSecond);
        }

        public async ValueTask DisposeAsync()
        {
            ConcurrencySemaphore.Dispose();
        }
    }

    /// <summary>
    /// Envelope para batch com metadata de retry
    /// </summary>
    private sealed record BatchEnvelope(List<DomainListEntry> Items, int RetryCount = 0);

    /// <summary>
    /// Rate limiter baseado em inter-arrival time — thread-safe
    /// Em vez de token bucket (burst -> sleep), calcula o instante minimo
    /// em que o proximo batch pode ser enviado (smooth pacing).
    /// 
    /// Exemplo com 2000 ops/s e batch=100:
    ///   intervalo minimo = 100/2000 * 1000 = 50ms entre batches
    ///   Se HTTP demora 80ms -> sem delay (ja passou do intervalo)
    ///   Se HTTP demora 10ms -> delay de 40ms (completa o intervalo)
    ///   Resultado: throughput estavel ~2000 ops/s, sem burst-then-sleep
    /// </summary>
    private sealed class SlidingWindowRateLimiter
    {
        private readonly double _msPerOp;
        private readonly Stopwatch _stopwatch;
        private double _nextAllowedMs;
        private readonly object _lock = new();

        public SlidingWindowRateLimiter(int maxOpsPerSecond)
        {
            _msPerOp = 1000.0 / maxOpsPerSecond;
            _stopwatch = Stopwatch.StartNew();
            _nextAllowedMs = _stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Aguarda ate o instante permitido para enviar itemCount items.
        /// Delay e apenas o gap restante (smooth pacing, sem burst).
        /// Jitter +/-10% no intervalo para dessincronizar particoes.
        /// </summary>
        public async Task WaitAsync(int itemCount, CancellationToken cancellationToken)
        {
            double waitMs;

            lock (_lock)
            {
                var now = _stopwatch.Elapsed.TotalMilliseconds;
                var intervalMs = _msPerOp * itemCount;

                // Jitter +/-10% no intervalo para dessincronizar particoes
                var jitterFraction = (Random.Shared.NextDouble() * 0.2) - 0.1;
                intervalMs *= (1.0 + jitterFraction);

                if (now >= _nextAllowedMs)
                {
                    // Ja passou do instante permitido — enviar imediatamente
                    // Nao acumular credito ilimitado: resetar para agora
                    _nextAllowedMs = now + intervalMs;
                    return;
                }

                // Ainda nao chegou o instante permitido — calcular delay
                waitMs = _nextAllowedMs - now;
                _nextAllowedMs += intervalMs;
            }

            if (waitMs > 1.0)
            {
                await Task.Delay((int)waitMs, cancellationToken);
            }
        }
    }

    public ParallelBatchManager(
        ParallelImportConfig config,
        ILogger<ParallelBatchManager> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config;
        _logger = logger;
        _partitionConsumers = new ConcurrentDictionary<string, PartitionConsumer>();
        _globalSemaphore = new SemaphoreSlim(_config.MaxDegreeOfParallelism, _config.MaxDegreeOfParallelism);
        _globalRateLimiter = new SlidingWindowRateLimiter(_config.Throttling.GlobalLimitPerSecond);
        _metrics = new ParallelBatchManagerMetrics();
        _consumerTasks = [];
        _currentBatches = new ConcurrentDictionary<string, List<DomainListEntry>>();
        _partitionItemCounts = new ConcurrentDictionary<string, int>();
    }

    /// <summary>
    /// Produz batches a partir das entries e consome em paralelo via consumers por partição.
    /// Producer e consumers rodam simultaneamente — sem risco de deadlock.
    /// 
    /// Fluxo:
    /// 1. Consumers são criados sob demanda quando cada partição é descoberta
    /// 2. Producer itera entries, agrupa em batches de 100, escreve nos channels (await WriteAsync)
    /// 3. Quando producer termina, sinaliza Complete() nos channels
    /// 4. Aguarda todos os consumers finalizarem
    /// </summary>
    public async Task ProduceAndConsumeAsync(
        IEnumerable<DomainListEntry> entries,
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Pipeline] Starting produce/consume | Global concurrency: {MaxParallelism} | Per-partition concurrency: {PerPartition} | Rate limits: {GlobalRate} global, {PartitionRate}/partition ops/s",
            _config.MaxDegreeOfParallelism,
            _config.MaxConcurrencyPerPartition,
            _config.Throttling.GlobalLimitPerSecond,
            _config.Throttling.PartitionLimitPerSecond);

        var progressStopwatch = Stopwatch.StartNew();
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Progress reporter em background
        using var progressTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.ProgressReportIntervalMs));
        var progressTask = RunProgressReporterAsync(progressStopwatch, progressTimer, progressCts.Token);

        // Producer: itera entries, cria batches, escreve nos channels
        // Consumers são iniciados lazily em GetOrCreateConsumer
        await RunProducerAsync(entries, sendBatchFunc, cancellationToken);

        
        foreach (var kvp in _partitionConsumers)
        {
            kvp.Value.BatchChannel.Writer.Complete();
        }

        _logger.LogInformation(
            "[Producer] Completed. Produced {Items:N0} items into {Partitions} partitions. Waiting for consumers...",
            GetTotalQueuedItems(),
            _partitionConsumers.Count);

        // Aguardar todos os consumers
        var consumerSnapshot = _consumerTasks.ToArray();
        if (consumerSnapshot.Length > 0)
        {
            await Task.WhenAll(consumerSnapshot);
        }

        progressStopwatch.Stop();

        // Parar progress reporter
        await progressCts.CancelAsync();
        try { await progressTask; }
        catch (OperationCanceledException) { /* esperado */ }

        // Log final
        var totalMetrics = _metrics.GetTotalMetrics();
        _logger.LogInformation(
            "[Pipeline] Completed | Processed {Batches} batches ({Items:N0} items) | Throughput: {Throughput:F0} ops/s | Time: {Time} | Retries: {Retries} | Dropped: {Dropped} | Backoffs: {Backoffs}",
            _totalBatchesProcessed,
            _totalBatchesProcessed * 100,
            progressStopwatch.Elapsed.TotalSeconds > 0
                ? (_totalBatchesProcessed * 100) / progressStopwatch.Elapsed.TotalSeconds
                : 0,
            FormatTimeSpan(progressStopwatch.Elapsed),
            totalMetrics.RetriedBatches,
            totalMetrics.DroppedBatches,
            totalMetrics.BackoffEvents);

        // Log backoff stats se houve problemas
        var backoffStats = _metrics.GetBackoffStats();
        foreach (var kvp in backoffStats.Where(s => s.Value.BackoffCount > 0))
        {
            _logger.LogWarning(
                "[Pipeline] Partition {Partition} had {BackoffCount} backoff events | Retried: {Retried} | Dropped: {Dropped}",
                kvp.Key,
                kvp.Value.BackoffCount,
                kvp.Value.RetriedBatches,
                kvp.Value.DroppedBatches);
        }
    }

    /// <summary>
    /// Producer task: itera entries, agrupa em batches de 100, escreve nos channels.
    /// Consumers são criados lazily em GetOrCreateConsumer — já rodando quando o primeiro write acontece.
    /// Usa await WriteAsync: se channel cheio, aguarda consumer drenar (sem deadlock).
    /// </summary>
    private async Task RunProducerAsync(
        IEnumerable<DomainListEntry> entries,
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var partitionKey = entry.PartitionKey;
            _metrics.RecordItemEnqueued(partitionKey);
            _partitionItemCounts.AddOrUpdate(partitionKey, 1, (_, count) => count + 1);

            var currentBatch = _currentBatches.GetOrAdd(partitionKey, _ => new List<DomainListEntry>(100));
            currentBatch.Add(entry);

            if (currentBatch.Count >= 100)
            {
                var consumer = GetOrCreateConsumer(partitionKey, sendBatchFunc, cancellationToken);
                var envelope = new BatchEnvelope(new List<DomainListEntry>(currentBatch));

                // await WriteAsync — se channel cheio, aguarda consumer drenar
                // Consumer já está rodando (iniciado em GetOrCreateConsumer), sem deadlock
                await consumer.BatchChannel.Writer.WriteAsync(envelope, cancellationToken);

                _metrics.RecordBatchCreated(partitionKey, consumer.BatchChannel.Reader.Count);
                currentBatch.Clear();
            }
        }

        // Flush batches parciais restantes (< 100 items)
        foreach (var kvp in _currentBatches)
        {
            if (kvp.Value.Count > 0)
            {
                var consumer = GetOrCreateConsumer(kvp.Key, sendBatchFunc, cancellationToken);
                var envelope = new BatchEnvelope(new List<DomainListEntry>(kvp.Value));
                await consumer.BatchChannel.Writer.WriteAsync(envelope, cancellationToken);
                _metrics.RecordBatchCreated(kvp.Key, consumer.BatchChannel.Reader.Count);
                kvp.Value.Clear();
            }
        }
    }

    /// <summary>
    /// Progress reporter: loga throughput, ETA e backoff ativo periodicamente
    /// Roda até ser cancelado pelo método principal
    /// </summary>
    private async Task RunProgressReporterAsync(
        Stopwatch progressStopwatch,
        PeriodicTimer progressTimer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await progressTimer.WaitForNextTickAsync(cancellationToken))
            {
                LogFlushProgress(progressStopwatch.Elapsed);
            }
        }
        catch (OperationCanceledException)
        {
            // Esperado — cancelado quando todos os consumers terminam
        }
    }

    /// <summary>
    /// Consumer dedicado de uma partição com pipelining.
    /// Usa ConcurrencySemaphore para permitir MaxConcurrencyPerPartition batches
    /// in-flight simultaneamente, mascarando latencia HTTP.
    /// Aplica backoff exponencial LOCAL em caso de erro.
    /// </summary>
    private async Task RunPartitionConsumerAsync(
        PartitionConsumer consumer,
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken)
    {
        var partitionKey = consumer.PartitionKey;
        var currentBackoffMs = 0;
        var inflightTasks = new List<Task>();

        _logger.LogDebug("[Consumer:{Partition}] Started", partitionKey);

        try
        {
            await foreach (var envelope in consumer.BatchChannel.Reader.ReadAllAsync(cancellationToken))
            {
                if (envelope.Items.Count == 0)
                    continue;

                // Backoff ativo? Aguardar ANTES de processar (não afeta outras partições)
                if (currentBackoffMs > 0)
                {
                    // Aguardar in-flight tasks antes de aplicar backoff
                    if (inflightTasks.Count > 0)
                    {
                        await Task.WhenAll(inflightTasks);
                        inflightTasks.Clear();
                    }

                    _logger.LogInformation(
                        "[Consumer:{Partition}] Backoff active: waiting {BackoffMs}ms before next batch",
                        partitionKey, currentBackoffMs);

                    await Task.Delay(currentBackoffMs, cancellationToken);
                }

                // Adquirir slot de concorrencia por particao (pipeline depth)
                await consumer.ConcurrencySemaphore.WaitAsync(cancellationToken);

                // Capturar envelope em variavel local para a closure
                var currentEnvelope = envelope;
                var currentBackoff = currentBackoffMs;

                var batchTask = Task.Run(async () =>
                {
                    try
                    {
                        var success = await ProcessBatchWithThrottlingAsync(
                            consumer, currentEnvelope, sendBatchFunc, cancellationToken);

                        if (success)
                        {
                            // Reset backoff progressivo
                            if (currentBackoff > 0)
                            {
                                var newBackoff = currentBackoff / 2;
                                if (newBackoff < _config.PartitionBackoffBaseMs / 2)
                                    newBackoff = 0;
                                consumer.CurrentBackoffMs = newBackoff;
                                _metrics.ResetPartitionBackoff(partitionKey);
                            }

                            _metrics.RecordBatchSucceeded(partitionKey);
                            Interlocked.Increment(ref _totalBatchesProcessed);
                        }
                        else
                        {
                            HandleBatchFailure(consumer, currentEnvelope, sendBatchFunc,
                                ref currentBackoff, cancellationToken);
                        }
                    }
                    finally
                    {
                        consumer.ConcurrencySemaphore.Release();
                    }
                }, cancellationToken);

                inflightTasks.Add(batchTask);

                // Limpar tasks completadas periodicamente
                inflightTasks.RemoveAll(t => t.IsCompleted);
            }

            // Aguardar in-flight tasks restantes
            if (inflightTasks.Count > 0)
            {
                await Task.WhenAll(inflightTasks);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[Consumer:{Partition}] Cancelled", partitionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Consumer:{Partition}] Consumer task failed", partitionKey);
        }

        _logger.LogDebug("[Consumer:{Partition}] Finished", partitionKey);
    }

    /// <summary>
    /// Trata falha de batch: aplica backoff exponencial e tenta retry inline
    /// </summary>
    private void HandleBatchFailure(
        PartitionConsumer consumer,
        BatchEnvelope envelope,
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        ref int currentBackoffMs,
        CancellationToken cancellationToken)
    {
        var partitionKey = consumer.PartitionKey;

        // Aplicar backoff exponencial
        currentBackoffMs = currentBackoffMs == 0
            ? _config.PartitionBackoffBaseMs
            : Math.Min(currentBackoffMs * 2, _config.PartitionBackoffMaxMs);

        consumer.CurrentBackoffMs = currentBackoffMs;
        _metrics.RecordPartitionBackoff(partitionKey, currentBackoffMs);

        if (envelope.RetryCount < _config.MaxPartitionRetries)
        {
            _logger.LogWarning(
                "[Consumer:{Partition}] Batch failed, will retry (attempt {Attempt}/{Max}) | Backoff: {BackoffMs}ms",
                partitionKey,
                envelope.RetryCount + 1,
                _config.MaxPartitionRetries,
                currentBackoffMs);

            _metrics.RecordBatchRetried(partitionKey);
        }
        else
        {
            _metrics.RecordBatchFailed(partitionKey);
            _metrics.RecordBatchDropped(partitionKey);
            Interlocked.Increment(ref _totalBatchesProcessed);

            _logger.LogError(
                "[Consumer:{Partition}] Batch dropped after exhausting {Max} retries",
                partitionKey, _config.MaxPartitionRetries);
        }
    }

    /// <summary>
    /// Processar batch respeitando rate limiting local e global + concurrency
    /// Retorna true se sucesso, false se falha
    /// 
    /// Camadas de throttling:
    /// 1. Rate limit por partição (smooth pacing)
    /// 2. Rate limit global (smooth pacing)
    /// 3. Concurrency global (HTTP connections)
    /// </summary>
    private async Task<bool> ProcessBatchWithThrottlingAsync(
        PartitionConsumer consumer,
        BatchEnvelope envelope,
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken)
    {
        var partitionKey = consumer.PartitionKey;

        try
        {
            // 1. Rate limit por partição (smooth pacing)
            await consumer.RateLimiter.WaitAsync(envelope.Items.Count, cancellationToken);

            // 2. Rate limit global
            await _globalRateLimiter.WaitAsync(envelope.Items.Count, cancellationToken);

            // 3. Concurrency global (HTTP connections)
            await _globalSemaphore.WaitAsync(cancellationToken);
            try
            {
                await sendBatchFunc(envelope.Items);
                _metrics.UpdateQueueDepth(partitionKey, consumer.BatchChannel.Reader.Count);
                return true;
            }
            finally
            {
                _globalSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[Consumer:{Partition}] Batch processing failed ({ItemCount} items, attempt {Attempt})",
                partitionKey,
                envelope.Items.Count,
                envelope.RetryCount + 1);

            _metrics.RecordBatchFailed(partitionKey);
            return false;
        }
    }

    /// <summary>
    /// Obtém ou cria consumer para a partição.
    /// Quando um novo consumer é criado, sua task consumidora é iniciada imediatamente
    /// com um stagger delay aleatório para evitar thundering herd.
    /// </summary>
    private PartitionConsumer GetOrCreateConsumer(
        string partitionKey,
        Func<List<DomainListEntry>, Task> sendBatchFunc,
        CancellationToken cancellationToken)
    {
        return _partitionConsumers.GetOrAdd(partitionKey, key =>
        {
            var consumer = new PartitionConsumer(
                key,
                _config.ChannelCapacityPerPartition,
                _config.MaxConcurrencyPerPartition,
                _config.Throttling.PartitionLimitPerSecond);

            // Stagger delay para dessincronizar consumers (0-100ms)
            var staggerMs = Random.Shared.Next(0, 100);

            // Iniciar consumer task imediatamente — ReadAllAsync aguarda dados no channel
            var task = Task.Run(async () =>
            {
                if (staggerMs > 0)
                    await Task.Delay(staggerMs, cancellationToken);

                await RunPartitionConsumerAsync(consumer, sendBatchFunc, cancellationToken);
            }, cancellationToken);
            _consumerTasks.Add(task);

            _logger.LogDebug("[Producer] Started consumer for partition {Partition} (stagger: {StaggerMs}ms)", key, staggerMs);
            return consumer;
        });
    }

    private void LogFlushProgress(TimeSpan elapsed)
    {
        var totalBatchesProcessed = _totalBatchesProcessed;
        var itemsProcessed = totalBatchesProcessed * 100;
        var totalBatches = _partitionItemCounts.Values.Sum(c => c / 100 + (c % 100 > 0 ? 1 : 0));
        var percentComplete = totalBatches > 0 ? (totalBatchesProcessed * 100) / totalBatches : 0;
        var throughput = elapsed.TotalSeconds > 0 ? itemsProcessed / elapsed.TotalSeconds : 0;

        var itemsRemaining = (totalBatches - totalBatchesProcessed) * 100;
        var secondsRemaining = throughput > 0 ? itemsRemaining / throughput : 0;
        var eta = TimeSpan.FromSeconds(secondsRemaining);

        // Partições com backoff ativo
        var activeBackoffs = _partitionConsumers
            .Where(c => c.Value.CurrentBackoffMs > 0)
            .Select(c => $"{c.Key}({c.Value.CurrentBackoffMs}ms)")
            .ToList();

        if (activeBackoffs.Count > 0)
        {
            _logger.LogInformation(
                "[Phase 2] {Percent}% complete ({Processed:N0} items) | Throughput: {Throughput:F0} ops/s | ETA: {ETA} | Backoff active: {Partitions}",
                percentComplete,
                itemsProcessed,
                throughput,
                FormatTimeSpan(eta),
                string.Join(", ", activeBackoffs));
        }
        else
        {
            _logger.LogInformation(
                "[Phase 2] {Percent}% complete ({Processed:N0} items) | Throughput: {Throughput:F0} ops/s | ETA: {ETA}",
                percentComplete,
                itemsProcessed,
                throughput,
                FormatTimeSpan(eta));
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        else if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        else
            return $"{ts.Seconds}s";
    }

    /// <summary>
    /// Obter total de itens enfileirados
    /// </summary>
    public int GetTotalQueuedItems()
    {
        return _partitionItemCounts.Values.Sum();
    }

    /// <summary>
    /// Obter número de batches pendentes por partição
    /// </summary>
    public Dictionary<string, int> GetPendingBatchCounts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var kvp in _partitionConsumers)
        {
            counts[kvp.Key] = kvp.Value.BatchChannel.Reader.Count;
        }
        return counts;
    }

    /// <summary>
    /// Obter número de tasks ativas (consumers não finalizados)
    /// </summary>
    public int GetActiveTaskCount()
    {
        return _consumerTasks.Count(t => !t.IsCompleted);
    }

    /// <summary>
    /// Obter métricas do gerenciador
    /// </summary>
    public ParallelBatchManagerMetrics GetMetrics() => _metrics;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Completar channels que ainda não foram completados
        foreach (var kvp in _partitionConsumers)
        {
            kvp.Value.BatchChannel.Writer.TryComplete();
        }

        // Aguardar consumer tasks finalizarem (com timeout)
        var pendingTasks = _consumerTasks.ToArray();
        if (pendingTasks.Length > 0)
        {
            var allTasks = Task.WhenAll(pendingTasks);
            var completed = await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != allTasks)
            {
                _logger.LogWarning("Some consumer tasks did not complete within 30s during dispose");
            }
        }

        // Dispose de partition consumers
        foreach (var kvp in _partitionConsumers)
        {
            await kvp.Value.DisposeAsync();
        }

        _globalSemaphore.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

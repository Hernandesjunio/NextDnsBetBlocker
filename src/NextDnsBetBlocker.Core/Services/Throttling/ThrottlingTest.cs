using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NextDnsBetBlocker.Core
{
    public class Entity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
    }

    public record ThrottlingConfig(
        int GlobalLimitPerSecond,
        int PartitionLimitPerSecond);

    public record PartitionProcessingConfig(
        int BatchSize = 100,
        int FlushWorkerCount = 5)
    {
        public void Validate()
        {
            if (BatchSize <= 0)
                throw new ArgumentException("BatchSize deve ser maior que 0", nameof(BatchSize));
            
            if (FlushWorkerCount <= 0)
                throw new ArgumentException("FlushWorkerCount deve ser maior que 0", nameof(FlushWorkerCount));
        }
    }

    /// <summary>
    /// Configuração de degradação progressiva com circuit breaker
    /// </summary>
    public record AdaptiveDegradationConfig(
        bool Enabled = true,
        int DegradationPercentagePerError = 10,
        int MinimumDegradationPercentage = 80,
        int RecoveryIntervalSeconds = 60,
        int CircuitBreakerResetIntervalSeconds = 300)
    {
        public void Validate()
        {
            if (DegradationPercentagePerError <= 0 || DegradationPercentagePerError > 100)
                throw new ArgumentException("DegradationPercentagePerError deve estar entre 1 e 100");
            
            if (MinimumDegradationPercentage < 1 || MinimumDegradationPercentage > 100)
                throw new ArgumentException("MinimumDegradationPercentage deve estar entre 1 e 100");
            
            if (MinimumDegradationPercentage > 100 - DegradationPercentagePerError)
                throw new ArgumentException("MinimumDegradationPercentage deve permitir espaço para degradação");
            
            if (RecoveryIntervalSeconds <= 0)
                throw new ArgumentException("RecoveryIntervalSeconds deve ser maior que 0");
            
            if (CircuitBreakerResetIntervalSeconds <= 0)
                throw new ArgumentException("CircuitBreakerResetIntervalSeconds deve ser maior que 0");
        }
    }

    /// <summary>
    /// Estados possíveis do circuit breaker
    /// </summary>
    public enum CircuitBreakerState
    {
        Closed,      // Normal
        Degraded,    // Reduzido mas funciona
        Open,        // Falhou completamente (circuit breaker ativo)
    }

    /// <summary>
    /// Delegate para operações de armazenamento em lote
    /// </summary>
    /// <param name="partitionKey">Chave da partição</param>
    /// <param name="batch">Lote de entidades para processar</param>
    public delegate Task BatchStorageOperation(string partitionKey, List<Entity> batch);

    public class PartitionWorker
    {
        private readonly string _partitionKey;
        private readonly HierarchicalThrottler _throttler;
        private readonly Channel<Entity> _itemsChannel;
        private readonly Channel<List<Entity>> _batchesChannel;
        private readonly PartitionProcessingConfig _processingConfig;
        private readonly BatchStorageOperation _storageOperation;
        private readonly ShardingProcessorMetrics _metrics;

        public PartitionWorker(
            string partitionKey, 
            HierarchicalThrottler throttler,
            PartitionProcessingConfig processingConfig,
            BatchStorageOperation storageOperation,
            ShardingProcessorMetrics metrics)
        {
            _partitionKey = partitionKey;
            _throttler = throttler;
            _processingConfig = processingConfig;
            _storageOperation = storageOperation ?? throw new ArgumentNullException(nameof(storageOperation));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _itemsChannel = Channel.CreateUnbounded<Entity>();
            _batchesChannel = Channel.CreateUnbounded<List<Entity>>();
        }

        public ChannelWriter<Entity> Writer => _itemsChannel.Writer;

        public async Task StartAsync()
        {
            var batcherTask = BatcherWorkerAsync();
            var flushTasks = Enumerable.Range(0, _processingConfig.FlushWorkerCount)
                .Select(_ => FlushWorkerAsync())
                .ToList();

            await Task.WhenAll(
                batcherTask,
                Task.WhenAll(flushTasks)
            );
        }

        private async Task BatcherWorkerAsync()
        {
            var buffer = new List<Entity>(_processingConfig.BatchSize);

            await foreach (var item in _itemsChannel.Reader.ReadAllAsync())
            {
                buffer.Add(item);

                if (buffer.Count >= _processingConfig.BatchSize)
                {
                    await _batchesChannel.Writer.WriteAsync(buffer);
                    buffer = new List<Entity>(_processingConfig.BatchSize);
                }
            }

            if (buffer.Count > 0)
            {
                await _batchesChannel.Writer.WriteAsync(buffer);
            }

            _batchesChannel.Writer.Complete();
        }

        private async Task FlushWorkerAsync()
        {
            await foreach (var batch in _batchesChannel.Reader.ReadAllAsync())
            {
                var breaker = _throttler.GetCircuitBreakerState(_partitionKey);
                if (breaker == CircuitBreakerState.Open)
                {
                    Console.WriteLine($"[CIRCUIT BREAKER] Partição '{_partitionKey}' indisponível. Ignorando batch.");
                    continue;
                }

                try
                {
                    await _throttler.ExecuteAsync(_partitionKey, batch.Count, async () =>
                    {
                        await _storageOperation(_partitionKey, batch);
                    });

                    // Sucesso - registrar em métricas
                    _metrics.RecordBatchProcessed(_partitionKey, batch.Count);
                    _throttler.RecordSuccess(_partitionKey);
                }
                catch (Exception ex)
                {
                    // Falha - registrar em métricas
                    _metrics.RecordBatchFailed(_partitionKey, batch.Count);
                    _throttler.RecordError(_partitionKey, ex);
                    throw;
                }
            }
        }
    }

    public class ShardingProcessor
    {
        private readonly ConcurrentDictionary<string, PartitionWorker> _workers = new();
        private readonly HierarchicalThrottler _throttler;
        private readonly PartitionProcessingConfig _partitionProcessingConfig;
        private readonly BatchStorageOperation _storageOperation;
        private readonly ShardingProcessorMetrics _metrics;

        public ShardingProcessor(
            ThrottlingConfig throttlingConfig,
            PartitionProcessingConfig partitionProcessingConfig,
            BatchStorageOperation storageOperation,
            AdaptiveDegradationConfig? degradationConfig = null)
        {
            throttlingConfig = throttlingConfig ?? throw new ArgumentNullException(nameof(throttlingConfig));
            partitionProcessingConfig = partitionProcessingConfig ?? throw new ArgumentNullException(nameof(partitionProcessingConfig));
            storageOperation = storageOperation ?? throw new ArgumentNullException(nameof(storageOperation));

            partitionProcessingConfig.Validate();
            degradationConfig?.Validate();

            _metrics = new ShardingProcessorMetrics();
            _throttler = new HierarchicalThrottler(
                throttlingConfig.GlobalLimitPerSecond, 
                throttlingConfig.PartitionLimitPerSecond,
                degradationConfig,
                _metrics);

            _partitionProcessingConfig = partitionProcessingConfig;
            _storageOperation = storageOperation;
        }

        public async Task ProcessAsync(IEnumerable<Entity> dataSource)
        {
            var workerTasks = new List<Task>();

            foreach (var item in dataSource)
            {
                string pk = item.PartitionKey;

                var worker = _workers.GetOrAdd(pk, key =>
                {
                    var newWorker = new PartitionWorker(
                        key, 
                        _throttler,
                        _partitionProcessingConfig,
                        _storageOperation,
                        _metrics);

                    workerTasks.Add(newWorker.StartAsync());
                    return newWorker;
                });

                await worker.Writer.WriteAsync(item);
            }

            foreach (var worker in _workers.Values)
            {
                worker.Writer.Complete();
            }

            await Task.WhenAll(workerTasks);
        }

        /// <summary>
        /// Obter métricas consolidadas
        /// </summary>
        public ShardingProcessorMetricsSummary GetMetrics()
        {
            return _metrics.GetSummary();
        }
    }

    public class HierarchicalThrottler
    {
        private readonly ConcurrentDictionary<string, TokenBucket> _partitionBuckets = new();
        private readonly TokenBucket _globalBucket;
        private readonly int _originalPartitionLimit;
        private readonly AdaptiveDegradationConfig _degradationConfig;
        private readonly ConcurrentDictionary<string, PartitionDegradationState> _degradationStates = new();
        private readonly ShardingProcessorMetrics _metrics;

        public HierarchicalThrottler(
            int globalLimitPerSecond, 
            int partitionLimitPerSecond,
            AdaptiveDegradationConfig? degradationConfig = null,
            ShardingProcessorMetrics? metrics = null)
        {
            _globalBucket = new TokenBucket(globalLimitPerSecond);
            _originalPartitionLimit = partitionLimitPerSecond;
            _degradationConfig = degradationConfig ?? new AdaptiveDegradationConfig();
            _degradationConfig.Validate();
            _metrics = metrics ?? new ShardingProcessorMetrics();
        }

        public CircuitBreakerState GetCircuitBreakerState(string partitionKey)
        {
            if (!_degradationStates.TryGetValue(partitionKey, out var state))
                return CircuitBreakerState.Closed;

            return state.CircuitBreakerState;
        }

        public void RecordError(string partitionKey, Exception ex)
        {
            if (!_degradationConfig.Enabled)
                return;

            var state = _degradationStates.GetOrAdd(partitionKey, _ => new PartitionDegradationState());

            if (state.CircuitBreakerState == CircuitBreakerState.Open)
            {
                if (state.ShouldAttemptCircuitBreakerReset(_degradationConfig.CircuitBreakerResetIntervalSeconds))
                {
                    state.ResetCircuitBreaker();
                    Console.WriteLine($"[CIRCUIT BREAKER RESET] Partição '{partitionKey}' tentando se recuperar.");
                    _metrics.RecordCircuitBreakerReset(partitionKey);
                }
                else
                {
                    return;
                }
            }

            int previousLimit = state.CurrentDegradedLimit;
            state.RecordError(_originalPartitionLimit, _degradationConfig);

            int newLimit = state.CurrentDegradedLimit;

            if (newLimit != previousLimit)
            {
                Console.WriteLine($"[DEGRADATION] Partição '{partitionKey}': {previousLimit} → {newLimit} ops/seg");
                int degradationPercentage = (newLimit * 100) / _originalPartitionLimit;
                _metrics.RecordDegradation(partitionKey, degradationPercentage);
            }

            if (state.CurrentDegradedLimit <= _originalPartitionLimit * _degradationConfig.MinimumDegradationPercentage / 100)
            {
                if (state.CircuitBreakerState != CircuitBreakerState.Open)
                {
                    state.OpenCircuitBreaker();
                    Console.WriteLine($"[CIRCUIT BREAKER OPENED] Partição '{partitionKey}' indisponível. Parando tentativas por {_degradationConfig.CircuitBreakerResetIntervalSeconds}s");
                    _metrics.RecordCircuitBreakerOpening(partitionKey);
                }
            }
        }

        public void RecordSuccess(string partitionKey)
        {
            if (!_degradationConfig.Enabled)
                return;

            if (_degradationStates.TryGetValue(partitionKey, out var state))
            {
                if (state.CurrentDegradedLimit < _originalPartitionLimit && 
                    state.ShouldAttemptRecovery(_degradationConfig.RecoveryIntervalSeconds))
                {
                    state.RecoverGradually(_originalPartitionLimit);
                    Console.WriteLine($"[RECOVERY] Partição '{partitionKey}': {state.CurrentDegradedLimit} ops/seg");
                }
            }
        }

        private int GetEffectivePartitionLimit(string partitionKey)
        {
            if (!_degradationConfig.Enabled || !_degradationStates.TryGetValue(partitionKey, out var state))
                return _originalPartitionLimit;

            return state.CurrentDegradedLimit;
        }

        /// <summary>
        /// Obter métricas consolidadas
        /// </summary>
        public ShardingProcessorMetricsSummary GetMetrics()
        {
            return _metrics.GetSummary();
        }

        public async Task ExecuteAsync(string partitionKey, int recordCount, Func<Task> callback)
        {
            int effectiveLimit = GetEffectivePartitionLimit(partitionKey);
            
            var partitionBucket = _partitionBuckets.GetOrAdd(
                partitionKey, 
                _ => new TokenBucket(effectiveLimit));

            // Atualiza o bucket se o limite mudou
            if (partitionBucket.Capacity != effectiveLimit)
            {
                _partitionBuckets[partitionKey] = new TokenBucket(effectiveLimit);
            }

            await Task.WhenAll(
                _globalBucket.ConsumeAsync(recordCount),
                partitionBucket.ConsumeAsync(recordCount)
            );

            try
            {
                await callback();
            }
            catch
            {
                throw;
            }
        }
    }

    public class PartitionDegradationState
    {
        public int CurrentDegradedLimit { get; private set; }
        public CircuitBreakerState CircuitBreakerState { get; private set; } = CircuitBreakerState.Closed;
        
        private DateTime _lastErrorTime;
        private DateTime _lastSuccessTime;
        private DateTime _circuitBreakerOpenedTime;

        public void RecordError(int originalLimit, AdaptiveDegradationConfig config)
        {
            _lastErrorTime = DateTime.UtcNow;

            if (CircuitBreakerState == CircuitBreakerState.Open)
                return;

            int degradationReduction = originalLimit * config.DegradationPercentagePerError / 100;
            int minLimit = originalLimit * config.MinimumDegradationPercentage / 100;
            
            int newLimit = CurrentDegradedLimit - degradationReduction;
            CurrentDegradedLimit = Math.Max(minLimit, newLimit);
        }

        public void RecoverGradually(int originalLimit)
        {
            _lastSuccessTime = DateTime.UtcNow;
            
            if (CurrentDegradedLimit < originalLimit)
            {
                CurrentDegradedLimit = originalLimit;
            }
        }

        public bool ShouldAttemptRecovery(int recoveryIntervalSeconds)
        {
            return (DateTime.UtcNow - _lastErrorTime).TotalSeconds >= recoveryIntervalSeconds;
        }

        public void OpenCircuitBreaker()
        {
            CircuitBreakerState = CircuitBreakerState.Open;
            _circuitBreakerOpenedTime = DateTime.UtcNow;
        }

        public bool ShouldAttemptCircuitBreakerReset(int resetIntervalSeconds)
        {
            return CircuitBreakerState == CircuitBreakerState.Open &&
                   (DateTime.UtcNow - _circuitBreakerOpenedTime).TotalSeconds >= resetIntervalSeconds;
        }

        public void ResetCircuitBreaker()
        {
            CircuitBreakerState = CircuitBreakerState.Degraded;
            CurrentDegradedLimit = 0;
        }
    }

    public class TokenBucket
    {
        private readonly double _capacity;
        private readonly double _tokensPerMs;
        private double _availableTokens;
        private long _lastRefillTimestamp;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public int Capacity { get; }

        public TokenBucket(int tokensPerSecond)
        {
            Capacity = tokensPerSecond;
            _capacity = tokensPerSecond;
            _tokensPerMs = (double)tokensPerSecond / 1000.0;
            _availableTokens = tokensPerSecond;
            _lastRefillTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public async Task ConsumeAsync(int tokensToConsume)
        {
            while (true)
            {
                await _lock.WaitAsync();
                try
                {
                    Refill();
                    if (_availableTokens >= tokensToConsume)
                    {
                        _availableTokens -= tokensToConsume;
                        return;
                    }
                    double missing = tokensToConsume - _availableTokens;
                    int waitTimeMs = (int)Math.Ceiling(missing / _tokensPerMs);
                    await Task.Delay(waitTimeMs);
                }
                finally { _lock.Release(); }
            }
        }

        private void Refill()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long delta = now - _lastRefillTimestamp;
            if (delta > 0)
            {
                _availableTokens = Math.Min(_capacity, _availableTokens + (delta * _tokensPerMs));
                _lastRefillTimestamp = now;
            }
        }
    }
}

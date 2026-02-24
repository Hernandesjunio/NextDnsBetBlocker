using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NextDnsBetBlocker.Core
{
    public class Entity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
    }

    public record ThrottlingConfig(
        int GlobalLimitPerSecond,
        int PartitionLimitPerSecond);

    public record PartitionProcessingConfig(
        int BatchSize = 100,
        int FlushWorkerCount = 5,
        int ChannelCapacity = 500)
    {
        public void Validate()
        {
            if (BatchSize <= 0)
                throw new ArgumentException("BatchSize deve ser maior que 0", nameof(BatchSize));

            if (FlushWorkerCount <= 0)
                throw new ArgumentException("FlushWorkerCount deve ser maior que 0", nameof(FlushWorkerCount));

            if (ChannelCapacity <= 0)
                throw new ArgumentException("ChannelCapacity deve ser maior que 0", nameof(ChannelCapacity));
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
        private readonly ShardingProcessorProgress? _progress;
        private readonly IProgressReporter? _progressReporter;
        private readonly ILogger _logger;
        private readonly int _partitionLimit;

        private long _lastSecondTimestamp;
        private int _itemsProcessedInCurrentSecond;

        public PartitionWorker(
            string partitionKey, 
            HierarchicalThrottler throttler,
            PartitionProcessingConfig processingConfig,
            BatchStorageOperation storageOperation,
            ShardingProcessorMetrics metrics,
            ILogger logger,
            int partitionLimit,
            ShardingProcessorProgress? progress = null,
            IProgressReporter? progressReporter = null)
        {
            _partitionKey = partitionKey;
            _throttler = throttler;
            _processingConfig = processingConfig;
            _storageOperation = storageOperation ?? throw new ArgumentNullException(nameof(storageOperation));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _partitionLimit = partitionLimit;
            _progress = progress;
            _progressReporter = progressReporter;

            // Configurar opções de canal com limite (Backpressure)
            // ChannelCapacity é definido em "batches" na configuração.
            // itemsChannel armazena itens individuais -> capacidade = buffers * BatchSize (ex: 500 * 100 = 50k itens)
            int itemsCapacity = _processingConfig.ChannelCapacity * _processingConfig.BatchSize;
            var itemsOptions = new BoundedChannelOptions(itemsCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,  // Apenas BatcherWorker lê
                SingleWriter = false  // Vários threads podem escrever via ShardingProcessor
            };

            // batchesChannel armazena batches prontos -> capacidade = buffers (ex: 500 batches)
            var batchesOptions = new BoundedChannelOptions(_processingConfig.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false, // Múltiplos FlushWorkers leem
                SingleWriter = true   // Apenas BatcherWorker escreve
            };

            _itemsChannel = Channel.CreateBounded<Entity>(itemsOptions);
            _batchesChannel = Channel.CreateBounded<List<Entity>>(batchesOptions);
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
                // Se o disjuntor estiver aberto, aguardar em vez de descartar os dados
                while (_throttler.GetCircuitBreakerState(_partitionKey) == CircuitBreakerState.Open)
                {
                    await Task.Delay(1000); // Backoff simples enquanto aguarda recuperação
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

                    // Monitorar Throughput da Partição
                    CheckThroughput(batch.Count);

                    // Reportar progresso
                    _progress?.ReportProgress(batch.Count);
                    if (_progress != null && _progressReporter != null)
                    {
                        _progressReporter.Report(_progress.GetCurrentProgress());
                    }
                }
                catch (Exception ex)
                {
                    // Falha crítica no lote após retries
                    _metrics.RecordBatchFailed(_partitionKey, batch.Count);
                    _throttler.RecordError(_partitionKey, ex);

                    // Logar erro mas manter o worker vivo para próximos lotes
                    _logger.LogError(ex, 
                        "❌ Falha crítica no processamento de batch na partição {PartitionKey}. {Count} itens não foram salvos. Worker continua ativo.", 
                        _partitionKey, 
                        batch.Count);

                    // Não relançar para não matar o worker
                }
            }
        }

        private void CheckThroughput(int count)
        {
            long currentStartOfSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long lastRecordedSecond = Interlocked.Read(ref _lastSecondTimestamp);

            if (currentStartOfSecond > lastRecordedSecond)
            {
                if (Interlocked.CompareExchange(ref _lastSecondTimestamp, currentStartOfSecond, lastRecordedSecond) == lastRecordedSecond)
                {
                    // Novo segundo iniciado, reseta contador
                    Interlocked.Exchange(ref _itemsProcessedInCurrentSecond, 0);
                }
            }

            int currentTotal = Interlocked.Add(ref _itemsProcessedInCurrentSecond, count);

            // Registrar métrica de throughput máximo
            _metrics.RecordThroughput(_partitionKey, currentTotal);

            // Alerta se exceder 110% do limite (considerando burst permitido de 10%)
            // O Azure permite picos breves, mas sustentação acima do limite causa 429
            int alertThreshold = (int)(_partitionLimit * 1.1); 

            if (currentTotal > alertThreshold)
            {
                // Evita flood de logs - loga apenas a cada 100 itens excedentes ou algo assim?
                // Simplificação: logar sempre que exceder incrementos significativos para não travar IO de log
                if (currentTotal % 500 == 0 || currentTotal == alertThreshold + count) 
                {
                    _logger.LogWarning(
                        "⚠️ Partition {PartitionKey} throughput warning: {CurrentRate} items/sec (Limit: {Limit})", 
                        _partitionKey, 
                        currentTotal, 
                        _partitionLimit);
                }
            }
        }
    }

    public class ShardingProcessor
    {
        private readonly ConcurrentDictionary<string, PartitionWorker> _workers = new();
        private readonly HierarchicalThrottler _throttler;
        private readonly ThrottlingConfig _throttlingConfig; // Novo campo para guardar config
        private readonly PartitionProcessingConfig _partitionProcessingConfig;
        private readonly BatchStorageOperation _storageOperation;
        private readonly ShardingProcessorMetrics _metrics;
        private readonly ILoggerFactory _loggerFactory; // Guardar factory para criar loggers pros workers
        private IProgressReporter _progressReporter;
        private ShardingProcessorProgress? _progress;

        public ShardingProcessor(
            ThrottlingConfig throttlingConfig,
            PartitionProcessingConfig partitionProcessingConfig,
            BatchStorageOperation storageOperation,
            IProgressReporter progressReporter,
            ILoggerFactory loggerFactory,
            AdaptiveDegradationConfig? degradationConfig = null
            )
        {
            _throttlingConfig = throttlingConfig ?? throw new ArgumentNullException(nameof(throttlingConfig));
            partitionProcessingConfig = partitionProcessingConfig ?? throw new ArgumentNullException(nameof(partitionProcessingConfig));
            storageOperation = storageOperation ?? throw new ArgumentNullException(nameof(storageOperation));

            partitionProcessingConfig.Validate();
            degradationConfig?.Validate();

            // ... (resto do construtor)
            _loggerFactory = loggerFactory;
            _metrics = new ShardingProcessorMetrics();
            _throttler = new HierarchicalThrottler(
                throttlingConfig.GlobalLimitPerSecond, 
                throttlingConfig.PartitionLimitPerSecond,
                loggerFactory.CreateLogger<HierarchicalThrottler>(),
                degradationConfig,
                _metrics);

            _partitionProcessingConfig = partitionProcessingConfig;
            _storageOperation = storageOperation;
            _progressReporter = progressReporter;
        }

        /// <summary>
        /// Definir reporter de progresso
        /// </summary>
        public void SetProgressReporter(IProgressReporter progressReporter)
        {
            _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        }

        public async Task ProcessAsync(IEnumerable<Entity> dataSource)
        {
            var sourceList = dataSource.ToList();
            _progress = new ShardingProcessorProgress(sourceList.Count);

            // Hook progress events to reporter
            if (_progressReporter != null)
            {
                _progress.ProgressChanged += args => _progressReporter.Report(args);
            }

            var workerTasks = new List<Task>();

            foreach (var item in sourceList)
            {
                string pk = item.PartitionKey;

                var worker = _workers.GetOrAdd(pk, key =>
                {
                    var newWorker = new PartitionWorker(
                        key, 
                        _throttler,
                        _partitionProcessingConfig,
                        _storageOperation,
                        _metrics,
                        _loggerFactory.CreateLogger<PartitionWorker>(), // Injeta logger
                        _throttlingConfig.PartitionLimitPerSecond,      // Injeta limite
                        _progress,
                        _progressReporter);

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

            // Finalize progress
            if (_progress != null)
            {
                var finalProgress = _progress.Finalize();
                _progressReporter?.Report(finalProgress);
            }
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
        private readonly ILogger<HierarchicalThrottler> _logger;

        
        public HierarchicalThrottler(
            int globalLimitPerSecond, 
            int partitionLimitPerSecond,
            ILogger<HierarchicalThrottler> logger,
            AdaptiveDegradationConfig? degradationConfig = null,
            ShardingProcessorMetrics? metrics = null)
        {
            // Limit burst to 10% of rate (100ms) to prevent massive spikes at start of second
            int globalBurst = Math.Max(1, (int)(globalLimitPerSecond * 0.1));
            _globalBucket = new TokenBucket(globalLimitPerSecond, globalBurst);
            _originalPartitionLimit = partitionLimitPerSecond;
            _degradationConfig = degradationConfig ?? new AdaptiveDegradationConfig();
            _degradationConfig.Validate();
            _metrics = metrics ?? new ShardingProcessorMetrics();
            _logger = logger;
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
                    // Fix 2 (call-site): passa os parâmetros para inicializar no mínimo correto, não em 0
                    state.ResetCircuitBreaker(_originalPartitionLimit, _degradationConfig.MinimumDegradationPercentage);
                    _logger.LogInformation(
                        "Circuit breaker reset for partition {PartitionKey}. Attempting recovery",
                        partitionKey);
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
                _logger.LogWarning(
                    "Partition {PartitionKey} degraded: {PreviousLimit} → {NewLimit} ops/sec",
                    partitionKey,
                    previousLimit,
                    newLimit);
                int degradationPercentage = (newLimit * 100) / _originalPartitionLimit;
                _metrics.RecordDegradation(partitionKey, degradationPercentage);
            }

            if (state.CurrentDegradedLimit <= _originalPartitionLimit * _degradationConfig.MinimumDegradationPercentage / 100)
            {
                if (state.CircuitBreakerState != CircuitBreakerState.Open)
                {
                    state.OpenCircuitBreaker();
                    _logger.LogCritical(
                        "Circuit breaker opened for partition {PartitionKey}. Resource unavailable. Will retry after {ResetIntervalSeconds} seconds",
                        partitionKey,
                        _degradationConfig.CircuitBreakerResetIntervalSeconds);
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
                    if (state.RecoverGradually(_originalPartitionLimit))
                    {
                        _logger.LogInformation(
                            "Partition {PartitionKey} recovering. Limit restored to {CurrentLimit} ops/sec",
                            partitionKey,
                            state.CurrentDegradedLimit);
                    }
                }
            }
        }

        private int GetEffectivePartitionLimit(string partitionKey)
        {
            if (!_degradationConfig.Enabled || !_degradationStates.TryGetValue(partitionKey, out var state))
                return _originalPartitionLimit;

            // Guard: defesa em profundidade — evita TokenBucket com rate=0 em qualquer janela concorrente
            return Math.Max(1, state.CurrentDegradedLimit);
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

            // Limit burst to 10% of rate for partitions too
            int burst = Math.Max(1, (int)(effectiveLimit * 0.1));

            var partitionBucket = _partitionBuckets.GetOrAdd(
                partitionKey, 
                _ => new TokenBucket(effectiveLimit, burst));

            // Atualiza o bucket se o limite mudou
            if (partitionBucket.Rate != effectiveLimit)
            {
                _partitionBuckets[partitionKey] = new TokenBucket(effectiveLimit, burst);
                // Refresh reference
                partitionBucket = _partitionBuckets[partitionKey];
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
        private DateTime _lastRecoveryTime; // Novo campo para controlar frequência de recuperação
        private DateTime _circuitBreakerOpenedTime;

        public void RecordError(int originalLimit, AdaptiveDegradationConfig config)
        {
            _lastErrorTime = DateTime.UtcNow;

            // Se erro ocorreu, forçar delay antes de próxima tentativa de recovery
            // Isso evita que um erro pare o recovery e logo em seguida ele tente subir de novo muito rápido
            _lastRecoveryTime = DateTime.UtcNow; 

            if (CircuitBreakerState == CircuitBreakerState.Open)
                return;

            // Fix 1: Inicializa na primeira chamada — evita salto incorreto de 0 → minLimit no primeiro erro
            if (CurrentDegradedLimit == 0)
                CurrentDegradedLimit = originalLimit;

            int degradationReduction = originalLimit * config.DegradationPercentagePerError / 100;
            int minLimit = originalLimit * config.MinimumDegradationPercentage / 100;

            int newLimit = CurrentDegradedLimit - degradationReduction;
            CurrentDegradedLimit = Math.Max(minLimit, newLimit);
        }

        public bool RecoverGradually(int originalLimit)
        {
            _lastSuccessTime = DateTime.UtcNow;

            // Só recupera se passou tempo suficiente desde a última recuperação (ex: 5 segundos)
            // Isso cria uma escada suave: sobe um degrau, estabiliza, sobe outro
            if ((DateTime.UtcNow - _lastRecoveryTime).TotalSeconds < 5)
                return false;

            if (CurrentDegradedLimit < originalLimit)
            {
                // Recupera 10% do limite original por degrau
                int step = Math.Max(1, (int)(originalLimit * 0.1));
                int newLimit = CurrentDegradedLimit + step;

                // Evita ultrapassar o original
                int previousLimit = CurrentDegradedLimit;
                CurrentDegradedLimit = Math.Min(originalLimit, newLimit);

                if (CurrentDegradedLimit != previousLimit)
                {
                    _lastRecoveryTime = DateTime.UtcNow;

                    // Se recuperou totalmente, fecha o circuit breaker (estado normal)
                    if (CurrentDegradedLimit >= originalLimit)
                    {
                        CircuitBreakerState = CircuitBreakerState.Closed;
                    }
                    else
                    {
                        // Se ainda não está 100%, mantém como Degraded
                        CircuitBreakerState = CircuitBreakerState.Degraded;
                    }
                    return true;
                }
            }
            return false;
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

        // Fix 2: Aceita os parâmetros necessários para calcular o limite mínimo correto
        // Antes: CurrentDegradedLimit = 0 → TokenBucket(rate=0) → _tokensPerMs=0 → Task.Delay(Infinity) → crash
        public void ResetCircuitBreaker(int originalLimit, int minimumDegradationPercentage)
        {
            CircuitBreakerState = CircuitBreakerState.Degraded;
            CurrentDegradedLimit = Math.Max(1, originalLimit * minimumDegradationPercentage / 100);
        }
    }

    public class TokenBucket
    {
        private readonly double _capacity;
        private readonly double _tokensPerMs;
        private double _availableTokens;
        private long _lastRefillTimestamp;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public int Rate { get; }
        public int Capacity { get; }

        public TokenBucket(int tokensPerSecond, int? maxBurst = null)
        {
            Rate = tokensPerSecond;
            Capacity = maxBurst ?? tokensPerSecond;
            _capacity = Capacity;
            _tokensPerMs = (double)tokensPerSecond / 1000.0;
            // Fix 4: inicia com 0 tokens — evita burst de startup que, somado ao refill do 1º segundo,
            // poderia gerar até Rate+Capacity ops no primeiro segundo, excedendo o limite da Azure (~2000/s)
            _availableTokens = 0;
            _lastRefillTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public async Task ConsumeAsync(int tokensToConsume)
        {
            while (true)
            {
                int waitTimeMs;
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
                    waitTimeMs = (int)Math.Ceiling(missing / _tokensPerMs);
                }
                finally
                {
                    // Fix 3: lock liberado ANTES do await Task.Delay
                    // Antes: lock retido durante todo o delay → os outros FlushWorkers ficavam
                    // bloqueados em WaitAsync mesmo com tokens disponíveis para eles
                    _lock.Release();
                }

                await Task.Delay(waitTimeMs); // espera fora do lock
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NextDnsBetBlocker.Worker.Importer
{
    public class Entity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
    }

    /// <summary>
    /// Configuração de throttling (taxa de operações por segundo)
    /// </summary>
    public record ThrottlingConfig(
        int GlobalLimitPerSecond,
        int PartitionLimitPerSecond);

    /// <summary>
    /// Configuração de processamento de uma partição
    /// Define como batches são formados e processados em paralelo
    /// </summary>
    public record PartitionProcessingConfig(
        int BatchSize = 100,
        int FlushWorkerCount = 5)
    {
        /// <summary>
        /// Valida se a configuração é sensata
        /// </summary>
        public void Validate()
        {
            if (BatchSize <= 0)
                throw new ArgumentException("BatchSize deve ser maior que 0", nameof(BatchSize));
            
            if (FlushWorkerCount <= 0)
                throw new ArgumentException("FlushWorkerCount deve ser maior que 0", nameof(FlushWorkerCount));
        }
    }

    public class PartitionWorker
    {
        private readonly string _partitionKey;
        private readonly HierarchicalThrottler _throttler;
        private readonly Channel<Entity> _itemsChannel;
        private readonly Channel<List<Entity>> _batchesChannel;
        private readonly PartitionProcessingConfig _processingConfig;

        public PartitionWorker(
            string partitionKey, 
            HierarchicalThrottler throttler,
            PartitionProcessingConfig processingConfig)
        {
            _partitionKey = partitionKey;
            _throttler = throttler;
            _processingConfig = processingConfig;
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
                await _throttler.ExecuteAsync(_partitionKey, batch.Count, async () =>
                {
                    await SendToTableStorage(_partitionKey, batch);
                });
            }
        }

        private async Task SendToTableStorage(string pk, List<Entity> data) 
            => await Task.Delay(1000);
    }

    public class ShardingProcessor
    {
        private readonly ConcurrentDictionary<string, PartitionWorker> _workers = new();
        private readonly HierarchicalThrottler _throttler;
        private readonly PartitionProcessingConfig _partitionProcessingConfig;

        public ShardingProcessor(
            ThrottlingConfig throttlingConfig,
            PartitionProcessingConfig partitionProcessingConfig)
        {
            throttlingConfig = throttlingConfig ?? throw new ArgumentNullException(nameof(throttlingConfig));
            partitionProcessingConfig = partitionProcessingConfig ?? throw new ArgumentNullException(nameof(partitionProcessingConfig));
            
            partitionProcessingConfig.Validate();

            _throttler = new HierarchicalThrottler(
                throttlingConfig.GlobalLimitPerSecond, 
                throttlingConfig.PartitionLimitPerSecond);
            
            _partitionProcessingConfig = partitionProcessingConfig;
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
                        _partitionProcessingConfig);
                    
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
    }

    public class HierarchicalThrottler
    {
        private readonly ConcurrentDictionary<string, TokenBucket> _partitionBuckets = new();
        private readonly TokenBucket _globalBucket;
        private readonly int _maxPerPartition;

        public HierarchicalThrottler(int globalLimitPerSecond, int partitionLimitPerSecond)
        {
            _globalBucket = new TokenBucket(globalLimitPerSecond);
            _maxPerPartition = partitionLimitPerSecond;
        }

        public async Task ExecuteAsync(string partitionKey, int recordCount, Func<Task> callback)
        {
            var partitionBucket = _partitionBuckets.GetOrAdd(partitionKey, _ => new TokenBucket(_maxPerPartition));

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

    public class TokenBucket
    {
        private readonly double _capacity;
        private readonly double _tokensPerMs;
        private double _availableTokens;
        private long _lastRefillTimestamp;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public TokenBucket(int tokensPerSecond)
        {
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

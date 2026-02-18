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

    public class PartitionWorker
    {
        private readonly string _partitionKey;
        private readonly HierarchicalThrottler _throttler;
        private readonly Channel<object> _itemsChannel;
        private readonly Channel<List<object>> _batchesChannel;
        private readonly int _batchSize;
        private readonly int _flushWorkerCount;

        public PartitionWorker(
            string partitionKey, 
            HierarchicalThrottler throttler, 
            int batchSize = 100,
            int flushWorkerCount = 5)
        {
            _partitionKey = partitionKey;
            _throttler = throttler;
            _batchSize = batchSize;
            _flushWorkerCount = flushWorkerCount;
            _itemsChannel = Channel.CreateUnbounded<object>();
            _batchesChannel = Channel.CreateUnbounded<List<object>>();
        }

        public ChannelWriter<object> Writer => _itemsChannel.Writer;

        public async Task StartAsync()
        {
            var batcherTask = BatcherWorkerAsync();
            var flushTasks = Enumerable.Range(0, _flushWorkerCount)
                .Select(_ => FlushWorkerAsync())
                .ToList();

            await Task.WhenAll(
                batcherTask,
                Task.WhenAll(flushTasks)
            );
        }

        // Worker que agrupa itens em batches
        private async Task BatcherWorkerAsync()
        {
            var buffer = new List<object>(_batchSize);

            await foreach (var item in _itemsChannel.Reader.ReadAllAsync())
            {
                buffer.Add(item);

                if (buffer.Count >= _batchSize)
                {
                    await _batchesChannel.Writer.WriteAsync(buffer);
                    buffer = new List<object>(_batchSize);
                }
            }

            // Enfileira o último batch
            if (buffer.Count > 0)
            {
                await _batchesChannel.Writer.WriteAsync(buffer);
            }

            _batchesChannel.Writer.Complete();
        }

        // Múltiplos workers que consomem batches do canal e enviam em paralelo
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

        private async Task SendToTableStorage(string pk, List<object> data) => await Task.Delay(1000);//await Task.CompletedTask;
    }

    public class ShardingProcessor
    {
        private readonly ConcurrentDictionary<string, PartitionWorker> _workers = new();
        private readonly HierarchicalThrottler _throttler;
        private readonly int _batchSize;
        private readonly int _flushWorkerCount;

        public ShardingProcessor(
            int globalLimit, 
            int partitionLimit,
            int batchSize = 100,
            int flushWorkerCount = 5)
        {
            _throttler = new HierarchicalThrottler(globalLimit, partitionLimit);
            _batchSize = batchSize;
            _flushWorkerCount = flushWorkerCount;
        }

        public async Task ProcessAsync(IEnumerable<Entity> dataSource)
        {
            var workerTasks = new List<Task>();

            // Producer
            foreach (var item in dataSource)
            {
                string pk = item.PartitionKey;

                var worker = _workers.GetOrAdd(pk, key =>
                {
                    var newWorker = new PartitionWorker(
                        key, 
                        _throttler, 
                        _batchSize, 
                        _flushWorkerCount);
                    workerTasks.Add(newWorker.StartAsync());
                    return newWorker;
                });

                await worker.Writer.WriteAsync(item);
            }

            // Finalization
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

        public HierarchicalThrottler(int globalLimit, int partitionLimit)
        {
            _globalBucket = new TokenBucket(globalLimit);
            _maxPerPartition = partitionLimit;
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

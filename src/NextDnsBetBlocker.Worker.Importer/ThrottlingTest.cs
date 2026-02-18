
    using System;
using System;
using System.Collections.Concurrent;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
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
        private readonly List<object> _buffer = new(100);
        private readonly Channel<object> _channel;

        public PartitionWorker(string partitionKey, HierarchicalThrottler throttler)
        {
            _partitionKey = partitionKey;
            _throttler = throttler;
            // Unbounded ou Bounded dependendo da sua memória
            _channel = Channel.CreateUnbounded<object>();
        }

        public ChannelWriter<object> Writer => _channel.Writer;

        public async Task StartAsync()
        {
            await foreach (var item in _channel.Reader.ReadAllAsync())
            {
                _buffer.Add(item);

                if (_buffer.Count == 100)
                {
                    await FlushAsync();
                }
            }

            // Flush final quando o canal é fechado
            if (_buffer.Count > 0)
            {
                await FlushAsync();
            }
        }

        private async Task FlushAsync()
        {
            var batchToSend = _buffer.ToList(); // Copia o lote atual
            _buffer.Clear();

            // Aplica o Throttling (Hierárquico: Global + Partição)
            await _throttler.ExecuteAsync(_partitionKey, batchToSend.Count, async () =>
            {
                // Sua lógica de TableTransactionAction aqui
                await SendToTableStorage(_partitionKey, batchToSend);
            });
        }

        private Task SendToTableStorage(string pk, List<object> data) => Task.CompletedTask; // Implementar
    }

    public class ShardingProcessor
    {
        private readonly ConcurrentDictionary<string, PartitionWorker> _workers = new();
        private readonly HierarchicalThrottler _throttler;

        public ShardingProcessor(int globalLimit, int partitionLimit)
        {
            _throttler = new HierarchicalThrottler(globalLimit, partitionLimit);
        }

        public async Task ProcessAsync(IEnumerable<Entity> dataSource)
        {
            var workerTasks = new List<Task>();

            // Producer
            foreach (var item in dataSource)
            {
                string pk = item.PartitionKey; // Seu cálculo de Sharding

                var worker = _workers.GetOrAdd(pk, key =>
                {
                    var newWorker = new PartitionWorker(key, _throttler);
                    workerTasks.Add(newWorker.StartAsync()); // Inicia o consumer daquela partição
                    return newWorker;
                });

                await worker.Writer.WriteAsync(item);
            }

            // Finalização (Flush Global)
            foreach (var worker in _workers.Values)
            {
                worker.Writer.Complete(); // Sinaliza para os consumers terminarem o que resta
            }

            await Task.WhenAll(workerTasks);
        }

        //private string CalculateHash(object item) => "00"; // Sua lógica de hash
    }

    public class HierarchicalThrottler
    {
        private readonly ConcurrentDictionary<string, TokenBucket> _partitionBuckets = new();
        private readonly TokenBucket _globalBucket;
        private readonly int _maxPerPartition;

        public HierarchicalThrottler(int globalLimit, int partitionLimit)
        {
            // Balde único para a conta toda (Global)
            _globalBucket = new TokenBucket(globalLimit);
            _maxPerPartition = partitionLimit;
        }

        public async Task ExecuteAsync(string partitionKey, int recordCount, Func<Task> callback)
        {
            // 1. Obtém o balde da partição (ou cria um novo se não existir)
            var partitionBucket = _partitionBuckets.GetOrAdd(partitionKey, _ => new TokenBucket(_maxPerPartition));

            // 2. Tenta consumir do limite Global e do limite da Partição simultaneamente
            // A ordem aqui é importante para não "travar" tokens globais se a partição estiver cheia
            await Task.WhenAll(
                _globalBucket.ConsumeAsync(recordCount),
                partitionBucket.ConsumeAsync(recordCount)
            );

            try
            {
                await callback();
            }
            finally
            {
                // Opcional: Se quiser uma limpeza de memória agressiva, 
                // pode implementar um Timer que remove chaves antigas de _partitionBuckets
            }
        }
    }

    // A classe TokenBucket permanece a mesma da resposta anterior, 
    // pois ela já resolve o controle de tempo/créditos de forma eficiente.
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

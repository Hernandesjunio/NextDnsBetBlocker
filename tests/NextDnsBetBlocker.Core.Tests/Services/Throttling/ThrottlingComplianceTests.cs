using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NextDnsBetBlocker.Core;
using Xunit;
using Xunit.Abstractions;

namespace NextDnsBetBlocker.Core.Tests.Services.Throttling
{
    public class ThrottlingComplianceTests
    {
        private readonly ITestOutputHelper _output;

        public ThrottlingComplianceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task HierarchicalThrottler_ShouldRespectGlobalLimit_InFirstSecond()
        {
            // Arrange
            int globalLimitPerSecond = 1000;
            int partitionLimitPerSecond = 100;
            var mockLogger = new Mock<ILogger<HierarchicalThrottler>>();
            
            var throttler = new HierarchicalThrottler(
                globalLimitPerSecond, 
                partitionLimitPerSecond,
                mockLogger.Object);

            int totalProcessed = 0;
            int requestCount = 2000; // Request 2x limit
            var sw = Stopwatch.StartNew();
            
            var tasks = new Task[requestCount];
            for (int i = 0; i < requestCount; i++)
            {
                tasks[i] = throttler.ExecuteAsync("partition-1", 1, async () => 
                {
                    Interlocked.Increment(ref totalProcessed);
                    await Task.Yield();
                });
            }

            // Act
            // Allow processing to run for exactly 1 second
            await Task.Delay(1000);

            sw.Stop();
            int finalCount = totalProcessed;

            _output.WriteLine($"Processed {finalCount} items in {sw.ElapsedMilliseconds}ms. Limit: {globalLimitPerSecond}/s");

            // Assert
            // Standard TokenBucket with Capacity == Rate allows 2 * Rate in first second (Burst + Drip).
            // If the implementation is "naive", it will process ~2000.
            // If we want strict compliance, it should be close to 1000.
            // We expect this to FAIL currently if it processes significantly more than 1000 + delta.
            
            // Let's assert strict compliance to demonstrate the issue.
            // Allow 10% tolerance for timing jitter.
            Assert.True(finalCount <= globalLimitPerSecond * 1.2, 
                $"Processed {finalCount} items, which exceeds limit {globalLimitPerSecond} significantly (Bursting issue).");
        }
    }
}

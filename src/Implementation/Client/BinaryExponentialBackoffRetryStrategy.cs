using System;
using System.Threading.Tasks;

namespace WebSockets.Client
{
    using System.Net;
    using System.Threading;
    using Abstractions;
    internal class BinaryExponentialBackoffRetryStrategy : IRetryStrategy
    {
        private static readonly Random Random = new Random();
        private int _currentRetry = 0;

        private readonly int _slotTimeInMs;
        private readonly bool _truncated;
        private readonly int _slotTimesThresholdWhenTruncated;

        private readonly int _maxRetryCount;

        public BinaryExponentialBackoffRetryStrategy(
            int slotTimeInMs = 32, 
            bool truncated = false, 
            int slotTimesThresholdWhenTruncated = 16,
            int maxRetryCount = 5)
        {
            _slotTimeInMs = slotTimeInMs;
            _truncated = truncated;
            _slotTimesThresholdWhenTruncated = slotTimesThresholdWhenTruncated;
            _maxRetryCount = maxRetryCount;
        }

        public async Task Apply(Func<CancellationToken,Task> action, CancellationToken cancellationToken = default)
        {
            while (_currentRetry < _maxRetryCount && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await action(cancellationToken);
                }
                catch (Exception ex) when (ex is TimeoutException || ex is System.Net.Http.HttpRequestException || ex is WebException)
                {
                    await Delay();
                }
            }
        }

        private async Task Delay()
        {
            var numberOfSlotTimes = Math.Pow(2, _currentRetry++) - 1;

            var slotTimesThreshold = _truncated ? _slotTimesThresholdWhenTruncated : int.MaxValue;

            var numberOfSlotsToUse = (int)Math.Min(numberOfSlotTimes, slotTimesThreshold);

            var delay = numberOfSlotsToUse * _slotTimeInMs;

            var jitter = Random.Next(0, 100);

            await Task.Delay(delay);
        }
    }
}
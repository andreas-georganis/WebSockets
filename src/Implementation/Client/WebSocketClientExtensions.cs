using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebSockets.Client.Abstractions
{
    public static class WebSocketClientExtensions
    {
        public static async Task Reconnect(this IWebSocketClient webSocketClient, CancellationToken cancellationToken = default) 
            => await Reconnect(webSocketClient, new BinaryExponentialBackoffRetryStrategy());
            
        private static async Task Reconnect(this IWebSocketClient webSocketClient, IRetryStrategy retryStrategy, CancellationToken cancellationToken = default)
        {
            await retryStrategy.Apply(async cancellationToken => await webSocketClient.Connect(cancellationToken), cancellationToken);
        }
    }
}
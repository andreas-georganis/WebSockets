using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebSockets.Client.Abstractions
{
    public interface IWebSocketClient : IAsyncDisposable
    {
        Uri Uri { get; }

        Task Connect(CancellationToken  cancellationToken = default);

        Task Send<T>(T message, CancellationToken cancellationToken = default);

        IObservable<IWebSocketClient> ConnectionOpened { get; }

        IObservable<byte[]> MessageReceived { get; }

        IObservable<ErrorOccuredArgs> ErrorOccured { get; }

        IObservable<ConnectionClosedArgs> ConnectionClosed { get; }

        Task Close(CancellationToken cancellationToken = default);
    }
}
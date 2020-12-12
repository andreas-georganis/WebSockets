using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using Newtonsoft.Json;

namespace WebSockets.Client
{
    using Abstractions;
    public sealed class WebSocketClient : IWebSocketClient
    {
        private const int SendChunkSize = 1024;
        private const int ReceiveChunkSize = 1024;
        private readonly Subject<IWebSocketClient> _connectionOpenedSubject = new Subject<IWebSocketClient>();
        private readonly Subject<ConnectionClosedArgs> _connectionClosedSubject = new Subject<ConnectionClosedArgs>();
        private readonly Subject<ErrorOccuredArgs> _errorOccuredSubject = new Subject<ErrorOccuredArgs>();
        private readonly Subject<byte[]> _messageReceivedSubject = new Subject<byte[]>();

        private readonly Func<Uri, CancellationToken, Task<ClientWebSocket>> _webSocketClientFactory;

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1);

        private CancellationTokenSource _cancellationTokenSource;

        private ClientWebSocket _webSocketClient;

        private Task _receiverTask;

        public WebSocketClient(Uri uri)
        {
            Uri = uri;
            _webSocketClientFactory = NewWebSocketClient;
        }

        public Uri Uri { get; }

        public IObservable<IWebSocketClient> ConnectionOpened => _connectionOpenedSubject.AsObservable();

        public IObservable<byte[]> MessageReceived => _messageReceivedSubject.AsObservable();

        public IObservable<ErrorOccuredArgs> ErrorOccured => _errorOccuredSubject.AsObservable();

        public IObservable<ConnectionClosedArgs> ConnectionClosed => _connectionClosedSubject.AsObservable();

        public async Task Connect(CancellationToken cancellationToken = default)
        {
            _webSocketClient = await _webSocketClientFactory(Uri, cancellationToken);
            _connectionOpenedSubject.OnNext(this);

            _cancellationTokenSource = new CancellationTokenSource();
            //_cancellationTokenSource.Token.Register(()=> _webSocketClient.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "Away", CancellationToken.None));
            
            _receiverTask = StartReceiving(_cancellationTokenSource.Token);
        }

        public async Task Send<T>(T data, CancellationToken cancellationToken)
        {
            if (_webSocketClient.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("Cannot send when not in open state.");
            }

            await _sendLock.WaitAsync(cancellationToken);

            try
            {
                var dataInBytes = PrepareData(data);
                var dataChunks = (int)Math.Ceiling((double)dataInBytes.Length / SendChunkSize);

                for (var i = 0; i < dataChunks; i++)
                {
                    var offset = i*dataChunks;
                    var count = offset + SendChunkSize > dataInBytes.Length ? dataInBytes.Length - offset: SendChunkSize;

                    var lastMessage = i == (dataChunks - 1);

                    await _webSocketClient.SendAsync(new ArraySegment<byte>(dataInBytes,offset, count), WebSocketMessageType.Binary, lastMessage, cancellationToken);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task Close(CancellationToken cancellationToken = default)
        {
            if (_webSocketClient == null)
            {
                return;
            }

            if (_webSocketClient.State != WebSocketState.Open && _webSocketClient.State != WebSocketState.CloseReceived)
            {
                return;
            }

            await _webSocketClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);

            // wait until connection is closed
            while (_webSocketClient.State !=  WebSocketState.Closed);

            _cancellationTokenSource.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            await Close();
            _webSocketClient?.Abort();
            _webSocketClient?.Dispose();
            _webSocketClient = null;
        }

        private async Task<ClientWebSocket> NewWebSocketClient(Uri uri, CancellationToken cancellationToken = default)
        {
            var webSocketClient = new ClientWebSocket();
            await webSocketClient.ConnectAsync(uri, cancellationToken);
            return webSocketClient;
        }

        private async Task StartReceiving(CancellationToken cancellationToken)
        {
            try
            {
                await foreach ((var messageResult, var messageBody) in ReceiveWebSocketMessages(cancellationToken))
                {
                    if (messageResult.MessageType == WebSocketMessageType.Close)
                    {
                        var closeStatus = messageResult.CloseStatus.HasValue? messageResult.CloseStatus.Value: WebSocketCloseStatus.Empty;
                        var closeDescription = messageResult.CloseStatusDescription;
                        await _webSocketClient.CloseOutputAsync(closeStatus, closeDescription, CancellationToken.None);

                        _connectionClosedSubject.OnNext(new ConnectionClosedArgs((short)closeStatus, closeDescription));
                    }
                    else
                    {
                        _messageReceivedSubject.OnNext(messageBody.ToArray());
                    }
                }
            }
            catch(OperationCanceledException)
            {
                // ignore
            }
            catch(ObjectDisposedException)
            {
                // ignore
            }
            catch(WebSocketException)
            {
                // ignore
            }
            catch(Exception ex)
            {
                _errorOccuredSubject.OnNext(new ErrorOccuredArgs(ex, "Unexpected error occured."));
            }
        }

        private async IAsyncEnumerable<(WebSocketReceiveResult, IEnumerable<byte>)> ReceiveWebSocketMessages([EnumeratorCancellation]CancellationToken cancellationToken)
        {
            var buffer = WebSocket.CreateClientBuffer(ReceiveChunkSize, ReceiveChunkSize);
            while (!cancellationToken.IsCancellationRequested && _webSocketClient.State == WebSocketState.Open)
            {
                List<byte> bytes = new List<byte>();
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocketClient.ReceiveAsync(buffer, cancellationToken);
                    
                    bytes.AddRange(new ArraySegment<byte>(buffer.Array, 0, result.Count));
                }
                while (!result.EndOfMessage);

                yield return (result, bytes);
            }
        }

        private byte[] PrepareData<T>(T data)
        {
            switch (data)
            {
                case byte[] bytes:
                    return bytes;
                case string str:
                    return Encoding.UTF8.GetBytes(str);
                case null:
                    throw new ArgumentNullException(nameof(data));
                default:
                    var @string = JsonConvert.SerializeObject(data, Formatting.None);
                    return Encoding.UTF8.GetBytes(@string);
            }
        }

    }
}
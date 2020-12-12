using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading.Tasks.Dataflow;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace WebSockets.Server
{
    using Abstractions;
    public class WebSocketEchoServer : IWebSocketServer
    {
        private const string ClientHandshakeHeader = "Sec-WebSocket-Key:";
        private const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private static Func<string, byte[]> GetServerHandshakeResponseTemplate => 
        (base64Sha1Hash) => System.Text.Encoding.UTF8.GetBytes(
        "HTTP/1.1 101 Switching Protocols\r\n" +
        "Connection: Upgrade\r\n" +
        "Upgrade: websocket\r\n" +
        "Sec-WebSocket-Accept: " + base64Sha1Hash + "\r\n\r\n");
        private readonly TcpListener _tcpListener;
        private readonly CancellationTokenSource _cts;

        private readonly ExecutionDataflowBlockOptions _consumerOptions;
        private readonly DataflowLinkOptions _linkOptions;

        private readonly BufferBlock<WebSocket> _webSocketQueue;

        private readonly ConcurrentBag<WebSocket> _clients;

        public WebSocketEchoServer(int port)
        {
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _cts = new CancellationTokenSource();

            _clients = new ConcurrentBag<WebSocket>();
            _webSocketQueue = new BufferBlock<WebSocket>(new DataflowBlockOptions{ BoundedCapacity = 10});

            _linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

            _consumerOptions = new ExecutionDataflowBlockOptions
            { 
                BoundedCapacity = 1
            };
        }

        public async Task Start(CancellationToken cancellationToken = default)
        {
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            linkedSource.Token.Register(()=> _tcpListener.Stop());

            _tcpListener.Start();

            while(!linkedSource.Token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync().WithCancellation(linkedSource.Token);

                    var networkStream = tcpClient.GetStream();

                    while (!networkStream.DataAvailable);
                    while (tcpClient.Available < 3); // match against "get"

                    byte[] bytes = new byte[tcpClient.Available];
                    networkStream.Read(bytes, 0, tcpClient.Available);
                    string s = Encoding.UTF8.GetString(bytes);

                    if (Regex.IsMatch(s, "^GET", RegexOptions.IgnoreCase)) {
                        
                        string swk = Regex.Match(s, $"{ClientHandshakeHeader} (.*)").Groups[1].Value.Trim();
                        string swka = swk + Guid;
                        byte[] swkaSha1 = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
                        string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);

                        byte[] response = GetServerHandshakeResponseTemplate(swkaSha1Base64);

                        networkStream.Write(response, 0, response.Length);

                        var webSocket = WebSocket.CreateFromStream(networkStream, isServer:true, null, Timeout.InfiniteTimeSpan);

                        if (await _webSocketQueue.SendAsync(webSocket))
                        {
                            var actionBlock = new ActionBlock<WebSocket>(
                                (ws) => ProcessWebSocketClient(ws, cancellationToken: linkedSource.Token), _consumerOptions);

                            _webSocketQueue.LinkTo(actionBlock, _linkOptions);

                            _clients.Add(webSocket);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //ignore
                }
                catch(Exception)
                {
                    //maybe log the error
                }
            }
        }

        private async Task ProcessWebSocketClient(WebSocket webSocket, CancellationToken cancellationToken = default)
        {
            try
            {
                var buffer = WebSocket.CreateServerBuffer(1024);
                while(!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    List<byte> bytes = new List<byte>();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                        bytes.AddRange(new ArraySegment<byte>(buffer.Array, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (result.MessageType != WebSocketMessageType.Close)
                    {
                        await webSocket.SendAsync(new ArraySegment<byte>(buffer.Array, 0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken);
                    }
                    else if (webSocket.State == WebSocketState.CloseReceived)
                    {
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                //ignore
            }
            catch (WebSocketException wse)
            {
                if (wse.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    System.Diagnostics.Debug.WriteLine("Client connection closed prematurely");
                }
                else if (wse.WebSocketErrorCode == WebSocketError.InvalidState)
                {
                    System.Diagnostics.Debug.WriteLine("Invalid WebSocket state");
                }
            }
            
        }

        public async Task Stop(CancellationToken cancellationToken = default)
        {
            _webSocketQueue.Complete();
            await _webSocketQueue.Completion;
            
            while(!_clients.IsEmpty)
            {
                if (_clients.TryTake(out var client))
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                }
            }

            _cts.Cancel();
        }
    }
}
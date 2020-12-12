using System;
using WebSockets.Server;
using WebSockets.Client;
using System.Threading;
using System.Threading.Tasks;
using WebSockets.Client.Abstractions;
using System.Reactive.Linq;
using System.Reactive;

namespace ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var server = new WebSocketEchoServer(8888);

            var cts = new CancellationTokenSource();
             cts.CancelAfter(TimeSpan.FromSeconds(30));

            var client = new WebSocketClient(new Uri("ws://localhost:8888"));

            client.ConnectionOpened.SelectMany(OnConnectionOpened).Subscribe();
            client.MessageReceived.Subscribe(OnMessageReceived);
            client.ErrorOccured.Subscribe(OnErrorOccured);
            client.ConnectionClosed.Subscribe(OnConnectionClosed);

            var cts2 = new CancellationTokenSource();
            cts2.CancelAfter(TimeSpan.FromSeconds(20));

            await Task.WhenAll(server.Start(cts.Token), client.Connect(cts2.Token));
            await client.DisposeAsync();

            Console.Read();
        }

        private static void OnConnectionClosed(ConnectionClosedArgs obj)
        {
            Console.WriteLine("Connection closed!");
        }

        private static void OnErrorOccured(ErrorOccuredArgs obj)
        {
            Console.WriteLine("An error occured!");
        }

        private static void OnMessageReceived(byte[] obj)
        {
            Console.WriteLine("A message received!");
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(obj));
        }

        private static async Task<Unit> OnConnectionOpened(IWebSocketClient obj)
        {
            Console.WriteLine("Client connected!");

            for (var i = 0; i< 100; i++)
            {
                await obj.Send("Hello");
                await Task.Delay(TimeSpan.FromSeconds(i));
            }

            return Unit.Default;   
        }
    }
}

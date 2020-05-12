using System.Threading;
using System.Threading.Tasks;

namespace WebSockets.Server.Abstractions
{
    public interface IWebSocketServer
    {
        Task Start(CancellationToken cancellationToken = default);
        Task Stop(CancellationToken cancellationToken = default);
    }
}
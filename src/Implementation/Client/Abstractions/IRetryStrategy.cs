using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebSockets.Client.Abstractions
{
    internal interface IRetryStrategy
    {
         Task Apply(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
    }
}
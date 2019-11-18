using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class AdHocEventsHandler<T> : IStreamEventsHandler<T>
    {
        private readonly Func<ReadStreamQuery, ReadStreamResult<T>, CancellationToken, Task> handler;

        public AdHocEventsHandler(Func<ReadStreamQuery, ReadStreamResult<T>, CancellationToken, Task> handler)
        {
            this.handler = handler;
        }

        public Task HandleAsync(ReadStreamQuery query, ReadStreamResult<T> streamResult, CancellationToken cancellationToken) =>
            handler(query, streamResult, cancellationToken);
    }
}
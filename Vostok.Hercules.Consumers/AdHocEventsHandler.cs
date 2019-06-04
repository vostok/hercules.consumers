using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class AdHocEventsHandler : IStreamEventsHandler
    {
        private readonly Func<ReadStreamQuery, ReadStreamResult, CancellationToken, Task> handler;

        public AdHocEventsHandler(Func<ReadStreamQuery, ReadStreamResult, CancellationToken, Task> handler)
        {
            this.handler = handler;
        }

        public Task HandleAsync(ReadStreamQuery query, ReadStreamResult streamResult, CancellationToken cancellationToken) =>
            handler(query, streamResult, cancellationToken);
    }
}
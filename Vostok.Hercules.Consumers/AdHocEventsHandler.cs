using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Queries;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class AdHocEventsHandler : IStreamEventsHandler
    {
        private readonly Func<ReadStreamQuery, IList<HerculesEvent>, CancellationToken, Task> handler;

        public AdHocEventsHandler(Func<ReadStreamQuery, IList<HerculesEvent>, CancellationToken, Task> handler)
        {
            this.handler = handler;
        }

        public Task HandleAsync(ReadStreamQuery query, IList<HerculesEvent> events, CancellationToken cancellationToken) =>
            handler(query, events, cancellationToken);
    }
}
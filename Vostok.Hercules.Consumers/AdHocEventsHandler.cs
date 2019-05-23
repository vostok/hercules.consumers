using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class AdHocEventsHandler : IStreamEventsHandler
    {
        private readonly Func<StreamCoordinates, IList<HerculesEvent>, CancellationToken, Task> handler;

        public AdHocEventsHandler(Func<StreamCoordinates, IList<HerculesEvent>, CancellationToken, Task> handler)
        {
            this.handler = handler;
        }

        public Task HandleAsync(StreamCoordinates coordinates, IList<HerculesEvent> events, CancellationToken cancellationToken) =>
            handler(coordinates, events, cancellationToken);
    }
}
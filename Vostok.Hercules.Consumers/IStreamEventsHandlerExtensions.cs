using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public static class IStreamEventsHandlerExtensions
    {
        public static IStreamEventsHandler<HerculesEvent> ToGeneric(this IStreamEventsHandler streamEventsHandler) =>
            new GenericAdapter(streamEventsHandler);

        private class GenericAdapter : IStreamEventsHandler<HerculesEvent>
        {
            private readonly IStreamEventsHandler streamEventsHandler;

            public GenericAdapter(IStreamEventsHandler streamEventsHandler)
            {
                this.streamEventsHandler = streamEventsHandler;
            }

            public Task HandleAsync(ReadStreamQuery query, ReadStreamResult<HerculesEvent> streamResult, CancellationToken cancellationToken) =>
                streamEventsHandler.HandleAsync(query, streamResult.FromGenericResult(), cancellationToken);
        }
    }
}
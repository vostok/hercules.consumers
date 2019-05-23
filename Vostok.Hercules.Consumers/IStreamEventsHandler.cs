using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public interface IStreamEventsHandler
    {
        [NotNull]
        Task HandleAsync([NotNull] StreamCoordinates coordinates, [NotNull] IList<HerculesEvent> events, CancellationToken cancellationToken);
    }
}
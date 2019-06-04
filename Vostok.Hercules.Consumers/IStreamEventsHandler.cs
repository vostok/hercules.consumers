using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public interface IStreamEventsHandler
    {
        [NotNull]
        Task HandleAsync([NotNull] ReadStreamQuery query, [NotNull] ReadStreamResult streamResult, CancellationToken cancellationToken);
    }
}
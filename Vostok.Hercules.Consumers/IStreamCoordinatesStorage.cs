using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public interface IStreamCoordinatesStorage
    {
        [NotNull]
        Task<StreamCoordinates> GetCurrentAsync();

        [NotNull]
        Task AdvanceAsync([NotNull] StreamCoordinates to);
    }
}
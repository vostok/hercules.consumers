using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Consumers.Helpers;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class InMemoryCoordinatesStorage : IStreamCoordinatesStorage
    {
        private readonly SemaphoreSlim advanceSemaphore = new SemaphoreSlim(1, 1);
        private volatile StreamCoordinates coordinates;

        public InMemoryCoordinatesStorage(StreamCoordinates initialCoordinates) => coordinates = initialCoordinates;

        public InMemoryCoordinatesStorage()
            : this(StreamCoordinates.Empty)
        {
        }

        public Task<StreamCoordinates> GetCurrentAsync() => Task.FromResult(coordinates);

        public async Task AdvanceAsync(StreamCoordinates to)
        {
            if (!await advanceSemaphore.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
                return;

            try
            {
                var currentCoordinates = coordinates;

                if (!to.AdvancesOver(currentCoordinates))
                    return;

                var mergedCoordinates = currentCoordinates.MergeMaxWith(to);

                coordinates = mergedCoordinates;
            }
            finally
            {
                advanceSemaphore.Release();
            }
        }
    }
}
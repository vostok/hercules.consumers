using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Consumers.Helpers;
using Vostok.ZooKeeper.Client.Abstractions.Model;
using Vostok.ZooKeeper.Client.Abstractions.Model.Request;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class ZooKeeperCoordinatesStorage : IStreamCoordinatesStorage
    {
        private readonly ZooKeeperCoordinatesStorageSettings settings;

        private readonly SemaphoreSlim advanceSemaphore = new SemaphoreSlim(1, 1);

        public ZooKeeperCoordinatesStorage([NotNull] ZooKeeperCoordinatesStorageSettings settings)
            => this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public async Task<StreamCoordinates> GetCurrentAsync()
        {
            var getDataResult = await settings.Client.GetDataAsync(settings.NodePath).ConfigureAwait(false);
            if (getDataResult.Status == ZooKeeperStatus.NodeNotFound)
                return StreamCoordinates.Empty;

            return StreamCoordinatesSerializer.Deserialize(getDataResult.Data);
        }

        public async Task AdvanceAsync(StreamCoordinates to)
        {
            if (!await advanceSemaphore.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
                return;

            try
            {
                while (true)
                {
                    var getDataResult = await settings.Client.GetDataAsync(settings.NodePath).ConfigureAwait(false);

                    switch (getDataResult.Status)
                    {
                        case ZooKeeperStatus.Ok:
                            var currentCoordinates = StreamCoordinatesSerializer.Deserialize(getDataResult.Data ?? Array.Empty<byte>());
                            if (!to.AdvancesOver(currentCoordinates))
                                return;

                            var mergedCoordinates = StreamCoordinatesMerger.MergeMaxWith(currentCoordinates, to);
                            var mergedData = StreamCoordinatesSerializer.Serialize(mergedCoordinates);

                            var setDataRequest = new SetDataRequest(settings.NodePath, mergedData)
                            {
                                Version = getDataResult.Stat.Version
                            };

                            var setDataResult = await settings.Client.SetDataAsync(setDataRequest).ConfigureAwait(false);
                            if (setDataResult.Status == ZooKeeperStatus.VersionsMismatch)
                                continue;

                            setDataResult.EnsureSuccess();
                            break;

                        case ZooKeeperStatus.NodeNotFound:
                            var createRequest = new CreateRequest(settings.NodePath, CreateMode.Persistent)
                            {
                                Data = StreamCoordinatesSerializer.Serialize(to)
                            };
                            var createNodeResult = await settings.Client.CreateAsync(createRequest).ConfigureAwait(false);
                            if (createNodeResult.Status == ZooKeeperStatus.NodeAlreadyExists)
                                continue;

                            createNodeResult.EnsureSuccess();
                            break;

                        default:
                            getDataResult.EnsureSuccess();
                            break;
                    }
                }
            }
            finally
            {
                advanceSemaphore.Release();
            }
        }
    }
}
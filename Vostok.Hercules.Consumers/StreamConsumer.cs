using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Helpers.Extensions;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Consumers.Helpers;
using Vostok.Logging.Abstractions;

// ReSharper disable MethodSupportsCancellation

#pragma warning disable 4014

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamConsumer
    {
        private readonly StreamConsumerSettings settings;
        private readonly ILog log;

        public StreamConsumer([NotNull] StreamConsumerSettings settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = (log ?? LogProvider.Get()).ForContext<StreamConsumer>();
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var coordinates = null as StreamCoordinates;
            var shardingSettings = null as StreamShardingSettings;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // (iloktionov): Catch-up with state for other shards on any change to our sharding settings:
                    var newShardingSettings = settings.ShardingSettingsProvider();
                    if (shardingSettings == null || !shardingSettings.Equals(newShardingSettings))
                    {
                        log.Info("Observed new sharding settings: shard with index {ShardIndex} from {ShardCount}. Syncing coordinates.",
                            newShardingSettings.ClientShardIndex, newShardingSettings.ClientShardCount);

                        coordinates = StreamCoordinatesMerger.Merge(
                            coordinates ?? StreamCoordinates.Empty,
                            await settings.CoordinatesStorage.GetCurrentAsync().ConfigureAwait(false));

                        log.Info("Updated coordinates from storage: {StreamCoordinates}", coordinates);

                        shardingSettings = newShardingSettings;
                    }

                    log.Info("Reading logical shard with index {ClientShard} from {ClientShardCount}.",
                        shardingSettings.ClientShardIndex, shardingSettings.ClientShardCount);

                    log.Debug("Current coordinates: {StreamCoordinates}", coordinates);

                    var eventsQuery = new ReadStreamQuery(settings.StreamName)
                    {
                        Coordinates = coordinates,
                        ClientShard = shardingSettings.ClientShardIndex,
                        ClientShardCount = shardingSettings.ClientShardCount,
                        Limit = settings.EventsBatchSize
                    };

                    var readResult = await settings.StreamClient.ReadAsync(eventsQuery, settings.EventsReadTimeout, cancellationToken).ConfigureAwait(false);

                    var events = readResult.Payload.Events;
                    if (events.Count == 0)
                    {
                        await Task.Delay(settings.DelayOnNoEvents, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    log.Info("Read {EventsCount} event(s) from Hercules stream '{StreamName}'.", events.Count, settings.StreamName);

                    await settings.EventsHandler.HandleAsync(coordinates, events, cancellationToken).ConfigureAwait(false);

                    var newCoordinates = coordinates = StreamCoordinatesMerger.Merge(coordinates, readResult.Payload.Next);

                    if (settings.AutoSaveCoordinates)
                        Task.Run(() => settings.CoordinatesStorage.AdvanceAsync(newCoordinates));
                }
                catch (Exception error)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    log.Error(error);

                    await Task.Delay(settings.DelayOnError, cancellationToken).SilentlyContinue().ConfigureAwait(false);
                }
            }
        }
    }
}

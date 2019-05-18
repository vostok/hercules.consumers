using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Helpers.Extensions;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Logging.Abstractions;

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
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var currentCoordinates = await settings.CoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
                    var currentShardingSettings = settings.ShardingSettingsProvider();

                    log.Debug("Reading logical shard with index {ClientShard} from {ClientShardCount}.",
                        currentShardingSettings.ClientShardIndex, currentShardingSettings.ClientShardCount);

                    log.Debug("Current coordinates: {StreamCoordinates}",
                        string.Join(", ", currentCoordinates.Positions.Select(p => $"{p.Partition} --> {p.Offset}")));

                    var eventsQuery = new ReadStreamQuery(settings.StreamName)
                    {
                        Coordinates = currentCoordinates,
                        ClientShard = currentShardingSettings.ClientShardIndex,
                        ClientShardCount = currentShardingSettings.ClientShardCount,
                        Limit = settings.EventsBatchSize
                    };

                    var readResult = await settings.StreamClient.ReadAsync(eventsQuery, settings.EventsReadTimeout, cancellationToken).ConfigureAwait(false);
                    var events = readResult.Payload.Events;
                    var newCoordinates = readResult.Payload.Next;

                    if (events.Count == 0)
                    {
                        await Task.Delay(settings.DelayOnNoEvents, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    log.Info("Read {EventsCount} event(s) from Hercules stream '{StreamName}'.", events.Count, settings.StreamName);

                    await settings.EventsHandler.HandleAsync(events).ConfigureAwait(false);

                    await settings.CoordinatesStorage.AdvanceAsync(currentCoordinates, newCoordinates).ConfigureAwait(false);
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

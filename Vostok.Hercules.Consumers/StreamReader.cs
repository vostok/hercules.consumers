using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Hercules.Consumers.Helpers;
using Vostok.Logging.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamReader
    {
        private readonly StreamReaderSettings settings;
        private readonly ILog log;

        public StreamReader([NotNull] StreamReaderSettings settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = (log ?? LogProvider.Get()).ForContext<StreamConsumer>();
        }

        public Task<(ReadStreamQuery query, ReadStreamResult result)> ReadAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken) =>
            ReadAsync(coordinates, shardingSettings, int.MaxValue, cancellationToken);

        public async Task<(ReadStreamQuery query, ReadStreamResult result)> ReadAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            int additionalLimit,
            CancellationToken cancellationToken)
        {
            log.Info(
                "Reading logical shard with index {ClientShard} from {ClientShardCount}.",
                shardingSettings.ClientShardIndex,
                shardingSettings.ClientShardCount);

            log.Debug("Current coordinates: {StreamCoordinates}.", coordinates);

            var eventsQuery = new ReadStreamQuery(settings.StreamName)
            {
                Coordinates = coordinates,
                ClientShard = shardingSettings.ClientShardIndex,
                ClientShardCount = shardingSettings.ClientShardCount,
                Limit = Math.Min(settings.EventsBatchSize, additionalLimit)
            };

            var readResult = await settings.StreamClient.ReadAsync(eventsQuery, settings.EventsReadTimeout, cancellationToken).ConfigureAwait(false);

            log.Info(
                "Read {EventsCount} event(s) from Hercules stream '{StreamName}'.",
                readResult.Payload.Events.Count,
                settings.StreamName);

            eventsQuery.Coordinates = StreamCoordinatesMerger.FixInitialCoordinates(coordinates, readResult.Payload.Next);

            return (eventsQuery, readResult);
        }

        public async Task<long> CountStreamRemainingEvents(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings)
        {
            try
            {
                var seekToEndQuery = new SeekToEndStreamQuery(settings.StreamName)
                {
                    ClientShard = shardingSettings.ClientShardIndex,
                    ClientShardCount = shardingSettings.ClientShardCount
                };

                var end = await settings.StreamClient.SeekToEndAsync(seekToEndQuery, settings.EventsReadTimeout).ConfigureAwait(false);

                return StreamCoordinatesMerger.Distance(coordinates, end.Payload.Next);
            }
            catch (Exception e)
            {
                log.Error(e, "Failed to count remaining events.");
                return 0;
            }
        }
    }
}
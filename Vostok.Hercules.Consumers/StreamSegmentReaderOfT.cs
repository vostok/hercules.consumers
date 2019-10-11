using System;
using System.Linq;
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
    public class StreamSegmentReader<T>
    {
        private readonly StreamReader<T> streamReader;
        private readonly StreamSegmentReaderSettings<T> settings;
        private readonly ILog log;
        private int? streamPartitionsCount;

        public StreamSegmentReader([NotNull] StreamSegmentReaderSettings<T> settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log = (log ?? LogProvider.Get()).ForContext<StreamSegmentReader<T>>();

            var streamReaderSettings = new StreamReaderSettings<T>(
                settings.StreamName,
                settings.StreamClient)
            {
                EventsReadBatchSize = settings.EventsReadBatchSize,
                EventsReadTimeout = settings.EventsReadTimeout,
                EventsReadAttempts = settings.EventsReadAttempts,
                DelayOnError = settings.DelayOnError
            };

            streamReader = new StreamReader<T>(streamReaderSettings, log);
        }

        public async Task<(ReadStreamQuery query, ReadStreamResult<T> result)> ReadAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken)
        {
            log.Info(
                "Reading logical shard with index {ClientShard} from {ClientShardCount}.",
                shardingSettings.ClientShardIndex,
                shardingSettings.ClientShardCount);

            log.Debug("Current coordinates: {StreamCoordinates}.", coordinates);

            coordinates = await GetShardCoordinates(coordinates, shardingSettings, cancellationToken).ConfigureAwait(false);
            log.Debug("Current shard coordinates: {StreamCoordinates}.", coordinates);

            streamPartitionsCount = streamPartitionsCount ?? await GetPartitionsCount(cancellationToken).ConfigureAwait(false);

            var current = coordinates.ToDictionary();
            foreach (var partition in coordinates.Positions.Select(p => p.Partition))
            {
                var start = current.ContainsKey(partition) ? current[partition].Offset : 0;
                var end = settings.End.ContainsKey(partition) ? settings.End[partition].Offset : 0;

                if (start < end)
                {
                    var count = end - start;

                    log.Info("Reading {EventsCount} events from partition #{Partition}.", count, partition);

                    var (query, result) = await streamReader.ReadAsync(
                            coordinates,
                            // ReSharper disable once PossibleInvalidOperationException
                            new StreamShardingSettings(partition, streamPartitionsCount.Value),
                            count,
                            cancellationToken)
                        .ConfigureAwait(false);

                    result.EnsureSuccess();

                    result = new ReadStreamResult<T>(
                        result.Status,
                        new ReadStreamPayload<T>(
                            result.Payload.Events,
                            coordinates.SetPosition(result.Payload.Next.Positions.Single())),
                        result.ErrorDetails);

                    query = new ReadStreamQuery(query.Name)
                    {
                        Coordinates = coordinates,
                        ClientShard = shardingSettings.ClientShardIndex,
                        ClientShardCount = shardingSettings.ClientShardCount
                    };

                    return (query, result);
                }
            }

            return (null, null);
        }

        private async Task<int> GetPartitionsCount(CancellationToken cancellationToken)
        {
            var allCoordinates = await GetShardCoordinates(StreamCoordinates.Empty, new StreamShardingSettings(0, 1), cancellationToken).ConfigureAwait(false);
            return allCoordinates.Positions.Length;
        }

        private async Task<StreamCoordinates> GetShardCoordinates(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken)
        {
            var (_, result) = await streamReader.ReadAsync(coordinates, shardingSettings, 1, cancellationToken).ConfigureAwait(false);

            var map = result.Payload.Next.ToDictionary();

            foreach (var position in coordinates.Positions)
            {
                if (map.ContainsKey(position.Partition))
                    map[position.Partition] = new StreamPosition
                    {
                        Partition = position.Partition,
                        Offset = position.Offset
                    };
            }

            return new StreamCoordinates(map.Values.ToArray());
        }
    }
}
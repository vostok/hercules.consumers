using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;
using Vostok.Commons.Helpers.Extensions;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Internal;
using Vostok.Hercules.Client.Serialization.Readers;
using Vostok.Hercules.Consumers.Helpers;
using Vostok.Logging.Abstractions;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Gauge;
using Vostok.Metrics.Primitives.Timer;
using BinaryBufferReader = Vostok.Hercules.Client.Serialization.Readers.BinaryBufferReader;
// ReSharper disable MethodSupportsCancellation
#pragma warning disable 4014

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class BatchesStreamConsumer<T>
    {
        private readonly BatchesStreamConsumerSettings<T> settings;
        private readonly ILog log;
        private readonly IMetricGroup1<IIntegerGauge> eventsMetric;
        private readonly IMetricGroup1<ITimer> iterationMetric;
        private readonly StreamApiRequestSender client;

        private StreamCoordinates coordinates;
        private StreamShardingSettings shardingSettings;

        private volatile bool restart;

        public BatchesStreamConsumer([NotNull] BatchesStreamConsumerSettings<T> settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log = (log ?? LogProvider.Get()).ForContext<BatchesStreamConsumer<T>>();

            var bufferPool = new BufferPool(settings.MaxPooledBufferSize, settings.MaxPooledBuffersPerBucket);
            client = new StreamApiRequestSender(settings.StreamApiCluster, log, bufferPool, settings.StreamApiClientAdditionalSetup);

            eventsMetric = settings.MetricContext?.CreateIntegerGauge("events", "type", new IntegerGaugeConfig {ResetOnScrape = true});
            iterationMetric = settings.MetricContext?.CreateSummary("iteration", "type", new SummaryConfig {Quantiles = new[] {0.5, 0.75, 1}});
            settings.MetricContext?.CreateFuncGauge("events", "type").For("remaining").SetValueProvider(CountStreamRemainingEvents);
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var newShardingSettings = settings.ShardingSettingsProvider();
                    if (shardingSettings == null || !shardingSettings.Equals(newShardingSettings))
                    {
                        log.Info(
                            "Observed new sharding settings: shard with index {ShardIndex} from {ShardCount}. Syncing coordinates.",
                            newShardingSettings.ClientShardIndex,
                            newShardingSettings.ClientShardCount);

                        shardingSettings = newShardingSettings;

                        restart = true;
                    }

                    if (restart)
                    {
                        await Restart(cancellationToken).ConfigureAwait(false);
                        restart = false;
                    }

                    using (iterationMetric?.For("time").Measure())
                    {
                        await MakeIteration(cancellationToken).ConfigureAwait(false);
                    }
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

        private async Task Restart(CancellationToken cancellationToken)
        {
            log.Info("Current coordinates: {StreamCoordinates}.", coordinates);

            var endCoordinates = await SeekToEndAsync(cancellationToken).ConfigureAwait(false);
            log.Info("End coordinates: {StreamCoordinates}.", endCoordinates);

            var storageCoordinates = await settings.CoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
            log.Info("Storage coordinates: {StreamCoordinates}.", storageCoordinates);

            // Note(kungurtsev): some coordinates are missing.
            if (endCoordinates.Positions.Any(p => storageCoordinates.Positions.All(pp => pp.Partition != p.Partition)))
            {
                log.Info("Returning end coordinates: {StreamCoordinates}.", endCoordinates);
                coordinates = endCoordinates;
                return;
            }

            log.Info("Returning storage coordinates: {StreamCoordinates}.", storageCoordinates);
            coordinates = storageCoordinates;
        }

        private async Task MakeIteration(CancellationToken cancellationToken)
        {
            RawReadStreamPayload result;
            StreamCoordinates queryCoordinates;

            using (iterationMetric?.For("read_time").Measure())
            {
                (queryCoordinates, result) = await ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                settings.OnBatchBegin?.Invoke(queryCoordinates);

                using (iterationMetric?.For("handle_time").Measure())
                {
                    HandleEvents(result);
                }

                coordinates = result.Next;

                settings.OnBatchEnd?.Invoke(coordinates);

                Task.Run(() => settings.CoordinatesStorage.AdvanceAsync(coordinates));
            }
            finally
            {
                result.Dispose();
            }
        }

        private void HandleEvents(RawReadStreamPayload result)
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            var reader = new BinaryBufferReader(result.Content.Array, result.Content.Offset)
            {
                Endianness = Endianness.Big
            };

            var count = reader.ReadInt32();

            for (var i = 0; i < count; i++)
            {
                var startPosition = reader.Position;

                try
                {
                    var @event = EventsBinaryReader.ReadEvent(reader, settings.EventBuilderProvider(reader));
                    settings.OnEvent(@event);
                }
                catch (Exception e)
                {
                    log.Error(e, "Failed to read event from position {Position}.", startPosition);

                    reader.Position = startPosition;
                    EventsBinaryReader.ReadEvent(reader, DummyEventBuilder.Instance);
                }
            }

            LogProgress(count);

            if (count == 0)
                Thread.Sleep(settings.DelayOnNoEvents);
        }

        private async Task<(StreamCoordinates query, RawReadStreamPayload result)> ReadAsync(CancellationToken cancellationToken = default)
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
                Limit = settings.EventsReadBatchSize
            };

            RawReadStreamResult readResult;
            
            do
            {
                readResult = await client.ReadAsync(eventsQuery, settings.ApiKeyProvider(), settings.EventsReadTimeout, cancellationToken).ConfigureAwait(false);
                if (!readResult.IsSuccessful)
                {
                    log.Error(
                        "Failed to read events from Hercules stream '{StreamName}'.",
                        settings.StreamName);
                    await Task.Delay(settings.DelayOnError, cancellationToken).SilentlyContinue().ConfigureAwait(false);
                }
            } while (!readResult.IsSuccessful);

            log.Info(
                "Read {BytesCount} byte(s) from Hercules stream '{StreamName}'.",
                readResult.Payload.Content.Count,
                settings.StreamName);

            eventsQuery.Coordinates = StreamCoordinatesMerger.FixQueryCoordinates(coordinates, readResult.Payload.Next);

            return (eventsQuery.Coordinates, readResult.Payload);
        }

        private async Task<StreamCoordinates> SeekToEndAsync(
            CancellationToken cancellationToken = default)
        {
            var seekToEndQuery = new SeekToEndStreamQuery(settings.StreamName)
            {
                ClientShard = shardingSettings.ClientShardIndex,
                ClientShardCount = shardingSettings.ClientShardCount
            };

            var end = await client.SeekToEndAsync(seekToEndQuery, settings.ApiKeyProvider(), settings.EventsReadTimeout, cancellationToken).ConfigureAwait(false);
            return end.Payload.Next;
        }

        private void LogProgress(int eventsIn)
        {
            log.Info("Consumer progress: events in: {EventsIn}.", eventsIn);
            eventsMetric?.For("in").Add(eventsIn);
            iterationMetric?.For("in").Report(eventsIn);
        }

        private double CountStreamRemainingEvents()
        {
            try
            {
                var endCoordinates = SeekToEndAsync().GetAwaiter().GetResult();

                var distance = StreamCoordinatesMerger.Distance(coordinates, endCoordinates);

                log.Info(
                    "Consumer progress: events remaining: {EventsRemaining}. Current coordinates: {CurrentCoordinates}, end coordinates: {EndCoordinates}.",
                    distance,
                    coordinates,
                    endCoordinates);

                return distance;
            }
            catch (Exception e)
            {
                log.Error(e, "Failed to count remaining events.");
                return 0;
            }
        }
    }
}
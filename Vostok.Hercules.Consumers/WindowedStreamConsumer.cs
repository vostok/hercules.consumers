using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;
using Vostok.Commons.Helpers.Extensions;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Hercules.Client.Internal;
using Vostok.Hercules.Client.Serialization.Readers;
using Vostok.Hercules.Consumers.Helpers;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Context;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Gauge;
using Vostok.Metrics.Primitives.Timer;
using BinaryBufferReader = Vostok.Hercules.Client.Serialization.Readers.BinaryBufferReader;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class WindowedStreamConsumer<T, TKey>
    {
        private readonly WindowedStreamConsumerSettings<T, TKey> settings;
        private readonly ILog log;
        private readonly IMetricGroup1<IIntegerGauge> eventsMetric;
        private readonly IMetricGroup1<IIntegerGauge> stateMetric;
        private readonly IMetricGroup1<ITimer> iterationMetric;
        private readonly StreamApiRequestSender client;
        private readonly Dictionary<TKey, Windows<T, TKey>> windows;

        private volatile StreamCoordinates leftCoordinates;
        private volatile StreamCoordinates rightCoordinates;
        private StreamShardingSettings shardingSettings;

        private volatile int iteration;
        private volatile bool restart;
        private volatile Task<(StreamCoordinates query, RawReadStreamPayload result)> readTask;
        private volatile Task saveCoordinatesTask;

        public WindowedStreamConsumer([NotNull] WindowedStreamConsumerSettings<T, TKey> settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log = (log ?? LogProvider.Get()).ForContext<WindowedStreamConsumer<T, TKey>>();

            var bufferPool = new BufferPool(settings.MaxPooledBufferSize, settings.MaxPooledBuffersPerBucket);
            client = new StreamApiRequestSender(settings.StreamApiCluster, log, bufferPool, settings.StreamApiClientAdditionalSetup);

            eventsMetric = settings.MetricContext?.CreateIntegerGauge("events", "type", new IntegerGaugeConfig { ResetOnScrape = true });
            stateMetric = settings.MetricContext?.CreateIntegerGauge("state", "type");
            iterationMetric = settings.MetricContext?.CreateSummary("iteration", "type", new SummaryConfig { Quantiles = new[] { 0.5, 0.75, 1 } });
            settings.MetricContext?.CreateFuncGauge("events", "type").For("remaining").SetValueProvider(() => CountStreamRemainingEvents());
            settings.MetricContext?.CreateFuncGauge("buffer", "type").For("rented_reader").SetValueProvider(() => BufferPool.Rented);
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
                        shardingSettings = newShardingSettings;
                        restart = true;
                    }

                    if (restart)
                    {
                        if (!await Restart().ConfigureAwait(false))
                            continue;
                        restart = false;
                    }

                    using (new OperationContextToken($"Iteration-{iteration++}"))
                    using (iterationMetric?.For("time").Measure())
                    {
                        await MakeIteration().ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    log.Error(error, "Failed to consume batch.");

                    await DelayOnError().ConfigureAwait(false);
                }
            }

            await (saveCoordinatesTask ?? Task.CompletedTask).ConfigureAwait(false);
            LogCoordinates("Final", leftCoordinates, rightCoordinates);
        }

        private async Task<bool> Restart()
        {
            using (new OperationContextToken("Restart"))
            {
                readTask = null;

                if (!await RestartCoordinates().ConfigureAwait(false))
                    return false;

                await RestartWindows().ConfigureAwait(false);

                return true;
            }
        }

        private async Task<bool> RestartCoordinates()
        {
            LogShardingSettings();
            LogCoordinates("Current", leftCoordinates, rightCoordinates);

            var end = await SeekToEndAsync().ConfigureAwait(false);
            if (!end.IsSuccessful)
            {
                log.Warn(
                    "Failed to get end coordinates. Status: {Status}. Error: '{Error}'.",
                    end.Status,
                    end.ErrorDetails);
                await DelayOnError().ConfigureAwait(false);
                return false;
            }

            var endCoordinates = end.Payload.Next;
            log.Info("End coordinates: {StreamCoordinates}.", endCoordinates);

            var leftStorageCoordinates = await settings.LeftCoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
            var rigthStorageCoordinates = await settings.RightCoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
            LogCoordinates("Storage", leftStorageCoordinates, rigthStorageCoordinates);

            if (endCoordinates.Positions.Any(p => rigthStorageCoordinates.Positions.All(pp => pp.Partition != p.Partition)))
            {
                leftCoordinates = rightCoordinates = endCoordinates;
                LogCoordinates("Some coordinates are missing. Returning end", leftCoordinates, rightCoordinates);
                return true;
            }

            leftCoordinates = leftStorageCoordinates;
            rightCoordinates = rigthStorageCoordinates;
            LogCoordinates("Returning storage", leftCoordinates, rightCoordinates);
            return true;
        }

        private Task RestartWindows()
        {
            windows.Clear();

            // TODO(kungurtsev): read events from left to right.

            return Task.CompletedTask;
        }

        private async Task MakeIteration()
        {
            var (queryCoordinates, result) = await (readTask ?? ReadAsync()).ConfigureAwait(false);

            try
            {
                rightCoordinates = result.Next;
                readTask = ReadAsync();

                HandleEvents(queryCoordinates, result);

                saveCoordinatesTask = Task.WhenAll(
                    settings.LeftCoordinatesStorage.AdvanceAsync(leftCoordinates),
                    settings.RightCoordinatesStorage.AdvanceAsync(rightCoordinates));
            }
            finally
            {
                result.Dispose();
            }
        }

        private void HandleEvents(StreamCoordinates queryCoordinates, RawReadStreamPayload result)
        {
            using (new OperationContextToken("HandleEvents"))
            using (iterationMetric?.For("handle_time").Measure())
            {
                FillWindows(queryCoordinates, result);
                FlushWindows();
            }
        }

        private void FillWindows(StreamCoordinates queryCoordinates, RawReadStreamPayload result)
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
                    AddEvent(@event, queryCoordinates);
                }
                catch (Exception e)
                {
                    log.Error(e, "Failed to read event from position {Position}.", startPosition);

                    reader.Position = startPosition;
                    EventsBinaryReader.ReadEvent(reader, DummyEventBuilder.Instance);
                }
            }

            log.Info("Consumer progress: events in: {EventsIn}.", count);
            eventsMetric?.For("in").Add(count);
            iterationMetric?.For("in").Report(count);

            if (count == 0)
                Thread.Sleep(settings.DelayOnNoEvents);
        }

        private void AddEvent(T @event, StreamCoordinates queryCoordinates)
        {
            var key = settings.KeyProvider(@event);
            if (!windows.ContainsKey(key))
                windows[key] = new Windows<T, TKey>(settings);
            if (!windows[key].AddEvent(@event, queryCoordinates))
                eventsMetric?.For("dropped").Increment();
        }

        private void FlushWindows()
        {
            var result = new WindowsFlushResult();
            var stale = new List<TKey>();

            foreach (var pair in windows)
            {
                var flushResult = pair.Value.Flush();
                result.MergeWith(flushResult);

                if (flushResult.EventsCount == 0
                    && DateTimeOffset.UtcNow - pair.Value.LastEventAdded > settings.WindowsTtl)
                    stale.Add(pair.Key);
            }

            foreach (var s in stale)
            {
                windows.Remove(s);
            }

            leftCoordinates = result.EventsCount == 0 ? rightCoordinates : result.FirstEventCoordinates;

            log.Info(
                "Consumer status: keys: {KeysCount}, windows: {WindowsCount}, events: {EventsCount}.",
                windows.Count,
                result.WindowsCount,
                result.EventsCount);
            stateMetric?.For("keys").Set(windows.Count);
            stateMetric?.For("windows").Set(result.WindowsCount);
            stateMetric?.For("events").Set(result.EventsCount);
        }

        private async Task<(StreamCoordinates query, RawReadStreamPayload result)> ReadAsync()
        {
            using (new OperationContextToken("ReadEvents"))
            using (iterationMetric?.For("read_time").Measure())
            {
                LogShardingSettings();
                log.Info("Current coordinates: {StreamCoordinates}.", rightCoordinates);

                var eventsQuery = new ReadStreamQuery(settings.StreamName)
                {
                    Coordinates = rightCoordinates,
                    ClientShard = shardingSettings.ClientShardIndex,
                    ClientShardCount = shardingSettings.ClientShardCount,
                    Limit = settings.EventsReadBatchSize
                };

                RawReadStreamResult readResult;

                do
                {
                    readResult = await client.ReadAsync(eventsQuery, settings.ApiKeyProvider(), settings.EventsReadTimeout).ConfigureAwait(false);
                    if (!readResult.IsSuccessful)
                    {
                        log.Warn(
                            "Failed to read events from Hercules stream '{StreamName}'. " +
                            "Status: {Status}. Error: '{Error}'.",
                            settings.StreamName,
                            readResult.Status,
                            readResult.ErrorDetails);
                        await DelayOnError().ConfigureAwait(false);
                    }
                } while (!readResult.IsSuccessful);

                log.Info(
                    "Read {BytesCount} byte(s) from Hercules stream '{StreamName}'.",
                    readResult.Payload.Content.Count,
                    settings.StreamName);

                eventsQuery.Coordinates = StreamCoordinatesMerger.FixQueryCoordinates(rightCoordinates, readResult.Payload.Next);

                return (eventsQuery.Coordinates, readResult.Payload);
            }
        }

        private double? CountStreamRemainingEvents()
        {
            var end = SeekToEndAsync().GetAwaiter().GetResult();
            if (!end.IsSuccessful)
            {
                log.Warn(
                    "Failed to count remaining events. Status: {Status}. Error: '{Error}'.",
                    end.Status,
                    end.ErrorDetails);
                return null;
            }

            var endCoordinates = end.Payload.Next;
            var distance = StreamCoordinatesMerger.Distance(rightCoordinates, endCoordinates);

            log.Info(
                "Consumer progress: events remaining: {EventsRemaining}. Current coordinates: {CurrentCoordinates}, end coordinates: {EndCoordinates}.",
                distance,
                rightCoordinates,
                endCoordinates);

            return distance;
        }

        private async Task<SeekToEndStreamResult> SeekToEndAsync()
        {
            var seekToEndQuery = new SeekToEndStreamQuery(settings.StreamName)
            {
                ClientShard = shardingSettings.ClientShardIndex,
                ClientShardCount = shardingSettings.ClientShardCount
            };

            var end = await client.SeekToEndAsync(seekToEndQuery, settings.ApiKeyProvider(), settings.EventsReadTimeout).ConfigureAwait(false);
            return end;
        }

        private async Task DelayOnError() =>
            await Task.Delay(settings.DelayOnError).SilentlyContinue().ConfigureAwait(false);

        private void LogCoordinates(string message, StreamCoordinates left, StreamCoordinates right) =>
            log.Info(message + " coordinates: left: {LeftCoordinates} right: {RightCoordinates}.", left, right);

        private void LogShardingSettings() =>
            log.Info(
                "Current sharding settings: shard with index {ShardIndex} from {ShardCount}.",
                shardingSettings.ClientShardIndex,
                shardingSettings.ClientShardCount);
    }
}
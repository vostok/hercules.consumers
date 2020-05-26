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

// ReSharper disable ParameterHidesMember

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
            windows = new Dictionary<TKey, Windows<T, TKey>>();

            eventsMetric = settings.MetricContext?.CreateIntegerGauge("events", "type", new IntegerGaugeConfig {ResetOnScrape = true});
            stateMetric = settings.MetricContext?.CreateIntegerGauge("state", "type");
            iterationMetric = settings.MetricContext?.CreateSummary("iteration", "type", new SummaryConfig {Quantiles = new[] {0.5, 0.75, 1}});
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

                    log.Error(error, "Failed to consume stream.");

                    await DelayOnError().ConfigureAwait(false);
                }
            }

            await (saveCoordinatesTask ?? Task.CompletedTask).ConfigureAwait(false);
            LogCoordinates("Final", leftCoordinates, rightCoordinates);
        }

        private async Task Restart()
        {
            using (new OperationContextToken("Restart"))
            {
                readTask = null;

                await RestartCoordinates().ConfigureAwait(false);

                try
                {
                    await RestartWindows().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.Error(e, "Failed to restart windows.");
                    windows.Clear();
                }
            }
        }

        private async Task RestartCoordinates()
        {
            LogShardingSettings();
            LogCoordinates("Current", leftCoordinates, rightCoordinates);

            var endCoordinates = await SeekToEndAsync(shardingSettings).ConfigureAwait(false);
            log.Info("End coordinates: {StreamCoordinates}.", endCoordinates);

            var leftStorageCoordinates = await settings.LeftCoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
            var rightStorageCoordinates = await settings.RightCoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
            LogCoordinates("Storage", leftStorageCoordinates, rightStorageCoordinates);

            if (endCoordinates.Positions.Any(p => leftStorageCoordinates.Positions.All(pp => pp.Partition != p.Partition)) ||
                endCoordinates.Positions.Any(p => rightStorageCoordinates.Positions.All(pp => pp.Partition != p.Partition)))
            {
                leftCoordinates = rightCoordinates = endCoordinates;
                LogCoordinates("Some coordinates are missing. Returning end", leftCoordinates, rightCoordinates);
                return;
            }

            leftCoordinates = leftStorageCoordinates;
            rightCoordinates = rightStorageCoordinates;
            LogCoordinates("Returning storage", leftCoordinates, rightCoordinates);
        }

        private async Task RestartWindows()
        {
            LogCoordinates("Current", leftCoordinates, rightCoordinates);

            windows.Clear();

            var partitionsCount = await GetPartitionsCount().ConfigureAwait(false);
            var coordinates = await GetShardCoordinates(leftCoordinates, shardingSettings).ConfigureAwait(false);
            log.Info("Current shard coordinates: {StreamCoordinates}.", coordinates);

            foreach (var position in coordinates.Positions)
            {
                var start = position.Offset;
                var end = rightCoordinates.Positions.Single(p => p.Partition == position.Partition).Offset;

                while (start < end)
                {
                    start = await RestartPartition(position.Partition, partitionsCount, start, end).ConfigureAwait(false);
                }
            }
        }

        private async Task<long> RestartPartition(int partition, int partitionsCount, long start, long end)
        {
            var query = new ReadStreamQuery(settings.StreamName)
            {
                Coordinates = new StreamCoordinates(new[] {new StreamPosition {Offset = start, Partition = partition}}),
                ClientShard = partition,
                ClientShardCount = partitionsCount,
                Limit = (int)Math.Min(end - start, settings.EventsReadBatchSize)
            };

            var (queryCoordinates, result) = await ReadAsync(query).ConfigureAwait(false);

            try
            {
                HandleEvents(queryCoordinates, result);

                foreach (var window in windows)
                    window.Value.Flush(true);
            }
            finally
            {
                result.Dispose();
            }

            return result.Next.Positions.Single().Offset;
        }

        private async Task MakeIteration()
        {
            LogCoordinates("Current", leftCoordinates, rightCoordinates);

            var (queryCoordinates, result) = await (readTask ?? ReadAsync()).ConfigureAwait(false);

            try
            {
                rightCoordinates = result.Next;
                readTask = ReadAsync();

                settings.OnBatchBegin?.Invoke(queryCoordinates);

                HandleEvents(queryCoordinates, result);
                FlushWindows();

                settings.OnBatchEnd?.Invoke(rightCoordinates);

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
            int count;

            using (new OperationContextToken("HandleEvents"))
            using (iterationMetric?.For("handle_time").Measure())
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                var reader = new BinaryBufferReader(result.Content.Array, result.Content.Offset)
                {
                    Endianness = Endianness.Big
                };

                count = reader.ReadInt32();

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
            }

            if (count == 0)
                Thread.Sleep(settings.DelayOnNoEvents);
        }

        private void AddEvent(T @event, StreamCoordinates queryCoordinates)
        {
            var key = settings.KeyProvider(@event);
            if (!windows.ContainsKey(key))
                windows[key] = new Windows<T, TKey>(key, settings);
            if (!windows[key].AddEvent(@event, queryCoordinates))
                eventsMetric?.For("dropped").Increment();
        }

        private void FlushWindows()
        {
            using (new OperationContextToken("FlushEvents"))
            using (iterationMetric?.For("flush_time").Measure())
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
                    windows.Remove(s);

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
        }

        private async Task<(StreamCoordinates query, RawReadStreamPayload result)> ReadAsync()
        {
            var eventsQuery = new ReadStreamQuery(settings.StreamName)
            {
                Coordinates = rightCoordinates,
                ClientShard = shardingSettings.ClientShardIndex,
                ClientShardCount = shardingSettings.ClientShardCount,
                Limit = settings.EventsReadBatchSize
            };

            return await ReadAsync(eventsQuery).ConfigureAwait(false);
        }

        private async Task<(StreamCoordinates query, RawReadStreamPayload result)> ReadAsync(ReadStreamQuery query)
        {
            using (new OperationContextToken("ReadEvents"))
            using (iterationMetric?.For("read_time").Measure())
            {
                log.Info(
                    "Reading {EventsCount} events from stream '{StreamName}'. " +
                    "Sharding settings: shard with index {ShardIndex} from {ShardCount}. " +
                    "Coordinates: {StreamCoordinates}.",
                    query.Limit,
                    settings.StreamName,
                    query.ClientShard,
                    query.ClientShardCount,
                    query.Coordinates);

                RawReadStreamResult readResult;

                do
                {
                    readResult = await client.ReadAsync(query, settings.ApiKeyProvider(), settings.EventsReadTimeout).ConfigureAwait(false);
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

                query.Coordinates = StreamCoordinatesMerger.FixQueryCoordinates(rightCoordinates, readResult.Payload.Next);

                return (query.Coordinates, readResult.Payload);
            }
        }

        private async Task<int> GetPartitionsCount()
        {
            var allCoordinates = await GetShardCoordinates(StreamCoordinates.Empty, new StreamShardingSettings(0, 1)).ConfigureAwait(false);
            return allCoordinates.Positions.Length;
        }

        private async Task<StreamCoordinates> GetShardCoordinates(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings)
        {
            var endCoordinates = await SeekToEndAsync(shardingSettings).ConfigureAwait(false);

            var map = endCoordinates.ToDictionary();

            foreach (var position in coordinates.Positions)
            {
                if (map.ContainsKey(position.Partition))
                    map[position.Partition] = position;
            }

            return new StreamCoordinates(map.Values.ToArray());
        }

        private double? CountStreamRemainingEvents()
        {
            if (rightCoordinates == null)
                return null;

            var end = SeekToEndAsync(shardingSettings).GetAwaiter().GetResult();
            var distance = StreamCoordinatesMerger.Distance(rightCoordinates, end);

            log.Info(
                "Consumer progress: events remaining: {EventsRemaining}. Current coordinates: {CurrentCoordinates}, end coordinates: {EndCoordinates}.",
                distance,
                rightCoordinates,
                end);

            return distance;
        }

        private async Task<StreamCoordinates> SeekToEndAsync(StreamShardingSettings shardingSettings)
        {
            var seekToEndQuery = new SeekToEndStreamQuery(settings.StreamName)
            {
                ClientShard = shardingSettings.ClientShardIndex,
                ClientShardCount = shardingSettings.ClientShardCount
            };

            SeekToEndStreamResult result;

            do
            {
                result = await client.SeekToEndAsync(seekToEndQuery, settings.ApiKeyProvider(), settings.EventsReadTimeout).ConfigureAwait(false);

                if (!result.IsSuccessful)
                {
                    log.Warn(
                        "Failed to seek to end for Hercules stream '{StreamName}'. " +
                        "Status: {Status}. Error: '{Error}'.",
                        settings.StreamName,
                        result.Status,
                        result.ErrorDetails);
                    await DelayOnError().ConfigureAwait(false);
                }
            } while (!result.IsSuccessful);

            return result.Payload.Next;
        }

        private async Task DelayOnError() =>
            await Task.Delay(settings.DelayOnError).SilentlyContinue().ConfigureAwait(false);

        private void LogCoordinates(string message, StreamCoordinates left, StreamCoordinates right) =>
            log.Info(message + " coordinates: left: {LeftCoordinates}, right: {RightCoordinates}.", left, right);

        private void LogShardingSettings() =>
            log.Info(
                "Current sharding settings: shard with index {ShardIndex} from {ShardCount}.",
                shardingSettings.ClientShardIndex,
                shardingSettings.ClientShardCount);
    }
}
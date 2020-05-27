﻿using System;
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
using Vostok.Metrics;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Gauge;
using Vostok.Metrics.Primitives.Timer;
using BinaryBufferReader = Vostok.Hercules.Client.Serialization.Readers.BinaryBufferReader;

// ReSharper disable InconsistentNaming
// ReSharper disable MethodSupportsCancellation

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class BatchesStreamConsumer<T>
    {
        private readonly BatchesStreamConsumerSettings<T> settings;
        private readonly ILog log;
        protected private readonly IMetricGroup1<IIntegerGauge> eventsMetric;
        protected private readonly IMetricGroup1<ITimer> iterationMetric;
        private readonly StreamApiRequestSender client;

        private StreamCoordinates coordinates;
        protected private StreamShardingSettings shardingSettings;

        private volatile int iteration;
        private volatile bool restart;
        private volatile Task<RawReadStreamPayload> readTask;
        private volatile Task saveCoordinatesTask;

        public BatchesStreamConsumer([NotNull] BatchesStreamConsumerSettings<T> settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log = (log ?? LogProvider.Get()).ForContext<BatchesStreamConsumer<T>>();

            var bufferPool = new BufferPool(settings.MaxPooledBufferSize, settings.MaxPooledBuffersPerBucket);
            client = new StreamApiRequestSender(settings.StreamApiCluster, log, bufferPool, settings.StreamApiClientAdditionalSetup);

            var metricContext = settings.MetricContext ?? new DevNullMetricContext();
            eventsMetric = metricContext.CreateIntegerGauge("events", "type", new IntegerGaugeConfig {ResetOnScrape = true});
            iterationMetric = metricContext.CreateSummary("iteration", "type", new SummaryConfig {Quantiles = new[] {0.5, 0.75, 1}});
            metricContext.CreateFuncGauge("events", "type").For("remaining").SetValueProvider(() => CountStreamRemainingEvents());
            metricContext.CreateFuncGauge("buffer", "type").For("rented_reader").SetValueProvider(() => BufferPool.Rented);
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
                        await Restart().ConfigureAwait(false);
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
            log.Info("Final coordinates: {StreamCoordinates}.", coordinates);
        }

        private async Task Restart()
        {
            using (new OperationContextToken("Restart"))
            {
                readTask = null;

                await RestartCoordinates().ConfigureAwait(false);

                settings.OnRestart?.Invoke(coordinates);
            }
        }

        private async Task RestartCoordinates()
        {
            LogShardingSettings();
            LogCoordinates("Current", coordinates);

            var endCoordinates = await SeekToEndAsync(shardingSettings).ConfigureAwait(false);
            LogCoordinates("End", endCoordinates);

            var storageCoordinates = await settings.CoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
            storageCoordinates = storageCoordinates.FilterBy(endCoordinates);
            LogCoordinates("Storage", storageCoordinates);

            if (storageCoordinates.Positions.Length < endCoordinates.Positions.Length)
            {
                log.Info("Some coordinates are missing. Returning end coordinates.");
                coordinates = endCoordinates;
                return;
            }

            log.Info("Returning storage coordinates.");
            coordinates = storageCoordinates;
        }

        private async Task MakeIteration()
        {
            var result = await (readTask ?? ReadAsync()).ConfigureAwait(false);

            try
            {
                var queryCoordinates = coordinates;
                settings.OnBatchBegin?.Invoke(queryCoordinates);

                coordinates = result.Next;
                readTask = ReadAsync();

                HandleEvents(result, queryCoordinates);

                settings.OnBatchEnd?.Invoke(coordinates);

                saveCoordinatesTask = settings.CoordinatesStorage.AdvanceAsync(coordinates);
            }
            finally
            {
                result.Dispose();
            }
        }

        protected private void HandleEvents(RawReadStreamPayload result, StreamCoordinates queryCoordinates)
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
                        settings.OnEvent?.Invoke(@event, queryCoordinates);
                    }
                    catch (Exception e)
                    {
                        log.Error(e, "Failed to read event from position {Position}.", startPosition);

                        reader.Position = startPosition;
                        EventsBinaryReader.ReadEvent(reader, DummyEventBuilder.Instance);
                    }
                }

                LogProgress(count);
            }

            if (count == 0)
                Thread.Sleep(settings.DelayOnNoEvents);
        }

        private async Task<RawReadStreamPayload> ReadAsync()
        {
            var query = new ReadStreamQuery(settings.StreamName)
            {
                Coordinates = coordinates,
                ClientShard = shardingSettings.ClientShardIndex,
                ClientShardCount = shardingSettings.ClientShardCount,
                Limit = settings.EventsReadBatchSize
            };

            return await ReadAsync(query).ConfigureAwait(false);
        }

        protected private async Task<RawReadStreamPayload> ReadAsync(ReadStreamQuery query)
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
                
                return readResult.Payload;
            }
        }

        private double? CountStreamRemainingEvents()
        {
            if (coordinates == null)
                return null;

            var end = SeekToEndAsync(shardingSettings).GetAwaiter().GetResult();
            var distance = StreamCoordinatesMerger.DistanceTo(coordinates, end);

            log.Info(
                "Consumer progress: events remaining: {EventsRemaining}. Current coordinates: {CurrentCoordinates}, end coordinates: {EndCoordinates}.",
                distance,
                coordinates,
                end);

            return distance;
        }

        // ReSharper disable once ParameterHidesMember
        protected private async Task<StreamCoordinates> SeekToEndAsync(StreamShardingSettings shardingSettings)
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

        private async Task DelayOnError()
        {
            await Task.Delay(settings.DelayOnError).SilentlyContinue().ConfigureAwait(false);
        }

        private void LogProgress(int eventsIn)
        {
            log.Info("Consumer progress: events in: {EventsIn}.", eventsIn);
            eventsMetric?.For("in").Add(eventsIn);
            iterationMetric?.For("in").Report(eventsIn);
        }

        protected void LogCoordinates(string message, StreamCoordinates streamCoordinates) =>
            log.Info($"{message} coordinates: {{StreamCoordinates}}.", streamCoordinates);

        private void LogShardingSettings() =>
            log.Info(
                "Current sharding settings: shard with index {ShardIndex} from {ShardCount}.",
                shardingSettings.ClientShardIndex,
                shardingSettings.ClientShardCount);
    }
}
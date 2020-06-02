using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Helpers.Extensions;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Logging.Abstractions;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Gauge;
using Vostok.Metrics.Primitives.Timer;

// ReSharper disable MethodSupportsCancellation

#pragma warning disable 4014

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamConsumer<T>
    {
        private readonly StreamConsumerSettings<T> settings;
        private readonly ILog log;
        private readonly StreamReader<T> streamReader;
        private readonly IMetricGroup1<IIntegerGauge> eventsMetric;
        private readonly IMetricGroup1<ITimer> iterationMetric;

        private StreamCoordinates coordinates;
        private StreamShardingSettings shardingSettings;

        private volatile bool restart;

        public StreamConsumer([NotNull] StreamConsumerSettings<T> settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = (log ?? LogProvider.Get()).ForContext<StreamConsumer<T>>();

            streamReader = new StreamReader<T>(
                new StreamReaderSettings<T>(
                    settings.StreamName,
                    settings.StreamClient)
                {
                    EventsReadTimeout = settings.EventsReadTimeout,
                    EventsReadBatchSize = settings.EventsReadBatchSize,
                    EventsReadAttempts = settings.EventsReadAttempts,
                    DelayOnError = settings.DelayOnError
                },
                log);

            eventsMetric = settings.MetricContext?.CreateIntegerGauge("events", "type", new IntegerGaugeConfig {ResetOnScrape = true});
            iterationMetric = settings.MetricContext?.CreateSummary("iteration", "type", new SummaryConfig {Quantiles = new[] {0.5, 0.75, 1}});
            settings.MetricContext?.CreateFuncGauge("events", "type").For("remaining").SetValueProvider(() => CountStreamRemainingEvents());
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // (iloktionov): Catch-up with state for other shards on any change to our sharding settings:
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

                    log.Error(error, "Failed to consume stream.");

                    await Task.Delay(settings.DelayOnError, cancellationToken).SilentlyContinue().ConfigureAwait(false);
                }
            }
        }

        private async Task Restart(CancellationToken cancellationToken)
        {
            log.Info("Current coordinates: {StreamCoordinates}.", coordinates);

            var endCoordinates = await streamReader.SeekToEndAsync(shardingSettings, cancellationToken).ConfigureAwait(false);
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
            ReadStreamQuery query;
            ReadStreamResult<T> result;
            using (iterationMetric?.For("read_time").Measure())
            {
                (query, result) = await streamReader.ReadAsync(coordinates, shardingSettings, cancellationToken).ConfigureAwait(false);
            }

            var events = result.Payload.Events;
            LogProgress(events.Count);

            if (events.Count != 0)
            {
                using (iterationMetric?.For("handle_time").Measure())
                {
                    await settings.EventsHandler.HandleAsync(query, result, cancellationToken).ConfigureAwait(false);
                }
            }

            coordinates = result.Payload.Next;

            if (events.Count == 0)
            {
                await Task.Delay(settings.DelayOnNoEvents, cancellationToken).ConfigureAwait(false);
            }

            Task.Run(() => settings.CoordinatesStorage.AdvanceAsync(coordinates));
        }

        private void LogProgress(int eventsIn)
        {
            log.Info("Consumer progress: events in: {EventsIn}.", eventsIn);
            eventsMetric?.For("in").Add(eventsIn);
            iterationMetric?.For("in").Report(eventsIn);
        }

        private long? CountStreamRemainingEvents()
        {
            var remaining = streamReader.CountStreamRemainingEventsAsync(coordinates, shardingSettings).GetAwaiter().GetResult();
            log.Info("Consumer progress: events remaining: {EventsRemaining}.", remaining);
            return remaining;
        }
    }
}
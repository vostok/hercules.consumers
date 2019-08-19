using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Helpers.Extensions;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Hercules.Consumers.Helpers;
using Vostok.Logging.Abstractions;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Gauge;
using Vostok.Metrics.Primitives.Timer;

// ReSharper disable MethodSupportsCancellation

#pragma warning disable 4014

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamConsumer
    {
        private readonly StreamConsumerSettings settings;
        private readonly ILog log;
        private readonly StreamReader streamReader;
        private readonly IMetricGroup1<IIntegerGauge> eventsMetric;
        private readonly IMetricGroup1<ITimer> iterationMetric;

        private StreamCoordinates coordinates;
        private StreamShardingSettings shardingSettings;

        private volatile bool restart;

        public StreamConsumer([NotNull] StreamConsumerSettings settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = (log ?? LogProvider.Get()).ForContext<StreamConsumer>();

            streamReader = new StreamReader(
                new StreamReaderSettings(
                    settings.StreamName,
                    settings.StreamClient)
                {
                    EventsReadTimeout = settings.EventsReadTimeout,
                    EventsBatchSize = settings.EventsBatchSize
                },
                log);

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

                    log.Error(error);

                    await Task.Delay(settings.DelayOnError, cancellationToken).SilentlyContinue().ConfigureAwait(false);
                }
            }
        }

        private async Task Restart(CancellationToken cancellationToken)
        {
            coordinates = await settings.CoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
            if (coordinates.Equals(StreamCoordinates.Empty))
                coordinates = await streamReader.SeekToEndAsync(shardingSettings, cancellationToken).ConfigureAwait(false);
            log.Info("Updated coordinates from storage: {StreamCoordinates}.", coordinates);
        }

        private async Task MakeIteration(CancellationToken cancellationToken)
        {
            ReadStreamQuery query;
            ReadStreamResult result;
            using (iterationMetric?.For("read_time").Measure())
            {
                (query, result) = await streamReader.ReadAsync(coordinates, shardingSettings, cancellationToken).ConfigureAwait(false);
            }

            var events = result.Payload.Events;
            LogProgress(events.Count);

            if (events.Count != 0 || settings.HandleWithoutEvents)
            {
                using (iterationMetric?.For("handle_time").Measure())
                {
                    await settings.EventsHandler.HandleAsync(query, result, cancellationToken).ConfigureAwait(false);
                }
            }

            var newCoordinates = coordinates = StreamCoordinatesMerger.MergeMax(coordinates, result.Payload.Next);

            if (events.Count == 0)
            {
                await Task.Delay(settings.DelayOnNoEvents, cancellationToken).ConfigureAwait(false);
            }

            Task.Run(() => settings.CoordinatesStorage.AdvanceAsync(newCoordinates));
        }

        private void LogProgress(int eventsIn)
        {
            log.Info("Consumer progress: events in: {EventsIn}.", eventsIn);
            eventsMetric?.For("in").Add(eventsIn);
            iterationMetric?.For("in").Report(eventsIn);
        }

        private double CountStreamRemainingEvents()
        {
            var remaining = streamReader.CountStreamRemainingEventsAsync(coordinates, shardingSettings).GetAwaiter().GetResult();
            log.Info("Consumer progress: events remaining: {EventsRemaining}.", remaining);
            return remaining;
        }
    }
}
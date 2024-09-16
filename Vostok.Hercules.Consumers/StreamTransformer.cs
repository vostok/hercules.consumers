using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Logging.Abstractions;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Gauge;
using Vostok.Metrics.Primitives.Timer;
using ITimer = Vostok.Metrics.Primitives.Timer.ITimer;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamTransformer
    {
        private readonly StreamTransformerSettings settings;
        private readonly ILog log;

        public StreamTransformer([NotNull] StreamTransformerSettings settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = (log ?? LogProvider.Get()).ForContext<StreamTransformer>();
        }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            var consumerSettings = new StreamConsumerSettings(
                settings.SourceStreamName,
                settings.StreamClient,
                new TransformingEventHandler(settings, log),
                settings.CoordinatesStorage,
                settings.ShardingSettingsProvider)
            {
                MetricContext = settings.MetricContext,
                EventsReadBatchSize = settings.EventsReadBatchSize,
                EventsReadTimeout = settings.EventsReadTimeout,
                EventsReadAttempts = settings.EventsReadAttempts,
                DelayOnError = settings.DelayOnError,
                DelayOnNoEvents = settings.DelayOnNoEvents
            };

            return new StreamConsumer(consumerSettings, log).RunAsync(cancellationToken);
        }

        private class TransformingEventHandler : IStreamEventsHandler
        {
            private readonly StreamTransformerSettings settings;
            private readonly StreamWriter writer;
            private readonly ILog log;

            private readonly List<HerculesEvent> buffer;
            private readonly IMetricGroup1<ITimer> iterationMetric;
            private IMetricGroup1<IIntegerGauge> eventsMetric;

            public TransformingEventHandler(StreamTransformerSettings settings, ILog log)
            {
                this.settings = settings;
                this.log = log;

                buffer = new List<HerculesEvent>();

                writer = new StreamWriter(
                    new StreamWriterSettings(settings.TargetStreamName, settings.GateClient)
                    {
                        DelayOnError = settings.DelayOnError,
                        EventsWriteBatchSize = settings.EventsWriteBatchSize,
                        EventsWriteTimeout = settings.EventsWriteTimeout
                    },
                    log);

                eventsMetric = settings.MetricContext?.CreateIntegerGauge("events", "type", new IntegerGaugeConfig {ResetOnScrape = true});
                iterationMetric = settings.MetricContext?.CreateSummary("iteration", "type", new SummaryConfig {Quantiles = new[] {0.5, 0.75, 1}});
            }

            public async Task HandleAsync(ReadStreamQuery query, ReadStreamResult streamResult, CancellationToken cancellationToken)
            {
                using (iterationMetric?.For("transform_time").Measure())
                {
                    var resultingEvents = streamResult.Payload.Events as IEnumerable<HerculesEvent>;

                    if (settings.Filter != null)
                        resultingEvents = resultingEvents.Where(settings.Filter);

                    if (settings.Transformer != null)
                        resultingEvents = resultingEvents.SelectMany(Transform);

                    buffer.Clear();
                    buffer.AddRange(resultingEvents);
                }

                if (buffer.Count == 0)
                    return;

                using (iterationMetric?.For("write_time").Measure())
                {
                    await writer.WriteEvents(buffer, cancellationToken).ConfigureAwait(false);
                }

                log.Info("Inserted {EventsCount} event(s) into target stream '{TargetStream}'.", buffer.Count, settings.TargetStreamName);
                eventsMetric?.For("out").Add(buffer.Count);

                buffer.Clear();
            }

            private IEnumerable<HerculesEvent> Transform(HerculesEvent @event)
            {
                try
                {
                    return settings.Transformer?.Invoke(@event) ?? Array.Empty<HerculesEvent>();
                }
                catch (Exception error)
                {
                    log.Warn(error);
                    eventsMetric?.For("error").Increment();
                    return Array.Empty<HerculesEvent>();
                }
            }
        }
    }
}
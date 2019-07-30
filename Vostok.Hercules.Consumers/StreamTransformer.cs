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
                EventsBatchSize = settings.EventsReadBatchSize,
                EventsReadTimeout = settings.EventsReadTimeout,
                DelayOnError = settings.DelayOnError,
                DelayOnNoEvents = settings.DelayOnNoEvents
            };

            return new StreamConsumer(consumerSettings, log).RunAsync(cancellationToken);
        }

        private class TransformingEventHandler : IStreamEventsHandler
        {
            private readonly StreamTransformerSettings settings;
            private readonly ILog log;

            private readonly List<HerculesEvent> buffer;
            private IMetricGroup1<IIntegerGauge> eventsMetric;
            private readonly IMetricGroup1<ITimer> iterationMetric;
            
            public TransformingEventHandler(StreamTransformerSettings settings, ILog log)
            {
                this.settings = settings;
                this.log = log;

                buffer = new List<HerculesEvent>();

                eventsMetric = settings.MetricContext?.CreateIntegerGauge("events", "type", new IntegerGaugeConfig {ResetOnScrape = true});
                iterationMetric = settings.MetricContext?.CreateSummary("iteration", "type", new SummaryConfig { Quantiles = new[] { 0.5, 0.75, 1 } });
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
                    await WriteEvents(buffer, cancellationToken).ConfigureAwait(false);
                }

                log.Info("Inserted {EventsCount} event(s) into target stream '{TargetStream}'.", buffer.Count, settings.TargetStreamName);
                eventsMetric?.For("out").Add(buffer.Count);
                
                buffer.Clear();
            }

            private async Task WriteEvents(IList<HerculesEvent> events, CancellationToken cancellationToken)
            {
                var pointer = 0;
                while (pointer < events.Count)
                {
                    try
                    {
                        var insertQuery = new InsertEventsQuery(
                            settings.TargetStreamName,
                            events.Skip(pointer).Take(settings.EventsWriteBatchSize).ToList());

                        var insertResult = await settings.GateClient
                            .InsertAsync(insertQuery, settings.EventsWriteTimeout, cancellationToken)
                            .ConfigureAwait(false);

                        insertResult.EnsureSuccess();

                        pointer += settings.EventsWriteBatchSize;

                        break;
                    }
                    catch (Exception e)
                    {
                        log.Error(e, "Failed to send aggregated events.");
                        await Task.Delay(settings.DelayOnError, cancellationToken).ConfigureAwait(false);
                    }
                }
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
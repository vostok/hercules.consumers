using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core.Model;
using Vostok.Commons.Collections;
using Vostok.Commons.Helpers.Disposable;
using Vostok.Commons.Helpers.Extensions;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Hercules.Client.Internal;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Context;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Gauge;
using Vostok.Metrics.Primitives.Timer;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamBinaryWriter
    {
        private readonly StreamBinaryWriterSettings settings;
        private readonly ILog log;
        private readonly IMetricGroup1<IIntegerGauge> eventsMetric;
        private readonly IMetricGroup1<ITimer> iterationMetric;
        private readonly GateRequestSender client;

        public StreamBinaryWriter([NotNull] StreamBinaryWriterSettings settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log = (log ?? LogProvider.Get()).ForContext<StreamBinaryWriter>();

            var bufferPool = new BufferPool(settings.MaxPooledBufferSize, settings.MaxPooledBuffersPerBucket);
            client = new GateRequestSender(settings.GateCluster, log, bufferPool, settings.GateClientAdditionalSetup);

            eventsMetric = settings.MetricContext?.CreateIntegerGauge("events", "type", new IntegerGaugeConfig { ResetOnScrape = true });
            iterationMetric = settings.MetricContext?.CreateSummary("iteration", "type", new SummaryConfig { Quantiles = new[] { 0.5, 0.75, 1 } });
            settings.MetricContext?.CreateFuncGauge("buffer", "type").For("rented_writer").SetValueProvider(() => BufferPool.Rented);
        }

        public async Task WriteAsync(string streamName, ArraySegment<byte> bytes, int eventsCount)
        {
            if (eventsCount == 0)
            {
                LogProgress(streamName, 0);
                return;
            }

            using (new OperationContextToken("WriteEvents"))
            using (iterationMetric?.For("write_time").Measure())
            {
                InsertEventsResult result;
                do
                {
                    result = await client.SendAsync(
                        streamName,
                        settings.ApiKeyProvider(),
                        new ValueDisposable<Content>(new Content(bytes), new EmptyDisposable()),
                        settings.EventsWriteTimeout,
                        CancellationToken.None).ConfigureAwait(false);

                    if (!result.IsSuccessful)
                    {
                        log.Warn(
                            "Failed to write events to Hercules stream '{StreamName}'. " +
                            "Status: {Status}. Error: '{Error}'.",
                            streamName,
                            result.Status,
                            result.ErrorDetails);
                        await DelayOnError().ConfigureAwait(false);
                    }
                } while (!result.IsSuccessful);

                LogProgress(streamName, eventsCount);
            }
        }

        private void LogProgress(string streamName, int eventsCount)
        {
            log.Info("Consumer progress: stream: {StreamName}, events out: {EventsOut}.", streamName, eventsCount);
            eventsMetric?.For("out").Add(eventsCount);
            iterationMetric?.For("out").Report(eventsCount);
        }

        private async Task DelayOnError()
        {
            await Task.Delay(settings.DelayOnError).SilentlyContinue().ConfigureAwait(false);
        }
    }
}
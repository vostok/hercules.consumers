using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Logging.Abstractions;

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
                EventsBatchSize = settings.EventsBatchSize,
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

            public TransformingEventHandler(StreamTransformerSettings settings, ILog log)
            {
                this.settings = settings;
                this.log = log;

                buffer = new List<HerculesEvent>();
            }

            public async Task HandleAsync(StreamCoordinates _, IList<HerculesEvent> events, CancellationToken cancellationToken)
            {
                var resultingEvents = events as IEnumerable<HerculesEvent>;

                if (settings.Filter != null)
                    resultingEvents = resultingEvents.Where(settings.Filter);

                if (settings.Transformer != null)
                    resultingEvents = resultingEvents.SelectMany(settings.Transformer);

                buffer.Clear();
                buffer.AddRange(resultingEvents);

                if (buffer.Count == 0)
                    return;

                var insertQuery = new InsertEventsQuery(settings.TargetStreamName, buffer);

                var insertResult = await settings.GateClient
                    .InsertAsync(insertQuery, settings.EventsWriteTimeout, cancellationToken)
                    .ConfigureAwait(false);

                insertResult.EnsureSuccess();

                log.Info("Inserted {EventsCount} event(s) into target stream '{TargetStream}'.", buffer.Count, settings.TargetStreamName);

                buffer.Clear();
            }
        }
    }
}
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Logging.Abstractions;

// ReSharper disable MethodSupportsCancellation

#pragma warning disable 4014

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamConsumer
    {
        private readonly StreamConsumer<HerculesEvent> consumer;

        public StreamConsumer([NotNull] StreamConsumerSettings settings, [CanBeNull] ILog log)
        {
            var convertedSettings = new StreamConsumerSettings<HerculesEvent>(
                settings.StreamName,
                settings.StreamClient.ToGenericClient(),
                settings.EventsHandler.ToGeneric(),
                settings.CoordinatesStorage,
                settings.ShardingSettingsProvider)
            {
                EventsReadBatchSize = settings.EventsReadBatchSize,
                EventsReadTimeout = settings.EventsReadTimeout,
                DelayOnError = settings.DelayOnError,
                EventsReadAttempts = settings.EventsReadAttempts,
                DelayOnNoEvents = settings.DelayOnNoEvents,
                HandleWithoutEvents = settings.HandleWithoutEvents,
                MetricContext = settings.MetricContext
            };

            consumer = new StreamConsumer<HerculesEvent>(convertedSettings, log);
        }

        public Task RunAsync(CancellationToken cancellationToken) =>
            consumer.RunAsync(cancellationToken);
    }
}
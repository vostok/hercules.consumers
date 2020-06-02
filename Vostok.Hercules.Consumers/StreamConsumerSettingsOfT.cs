using System;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;
using Vostok.Metrics;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamConsumerSettings<T>
    {
        public StreamConsumerSettings(
            [NotNull] string streamName,
            [NotNull] IHerculesStreamClient<T> streamClient,
            [NotNull] IStreamEventsHandler<T> eventsHandler,
            [NotNull] IStreamCoordinatesStorage coordinatesStorage,
            [NotNull] Func<StreamShardingSettings> shardingSettingsProvider)
        {
            StreamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
            StreamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
            EventsHandler = eventsHandler ?? throw new ArgumentNullException(nameof(eventsHandler));
            CoordinatesStorage = coordinatesStorage ?? throw new ArgumentNullException(nameof(coordinatesStorage));
            ShardingSettingsProvider = shardingSettingsProvider ?? throw new ArgumentNullException(nameof(shardingSettingsProvider));
        }

        [NotNull]
        public string StreamName { get; }

        [NotNull]
        public IHerculesStreamClient<T> StreamClient { get; }

        [NotNull]
        public IStreamEventsHandler<T> EventsHandler { get; }

        [NotNull]
        public IStreamCoordinatesStorage CoordinatesStorage { get; }

        [NotNull]
        public Func<StreamShardingSettings> ShardingSettingsProvider { get; }

        [CanBeNull]
        public IMetricContext MetricContext { get; set; }

        public int EventsReadBatchSize { get; set; } = ConsumersConstants.EventsReadBatchSize;

        public TimeSpan EventsReadTimeout { get; set; } = ConsumersConstants.EventsReadTimeout;

        public int EventsReadAttempts { get; set; } = ConsumersConstants.EventsReadAttempts;

        public TimeSpan DelayOnError { get; set; } = ConsumersConstants.DelayOnError;

        public TimeSpan DelayOnNoEvents { get; set; } = ConsumersConstants.DelayOnNoEvents;
    }
}
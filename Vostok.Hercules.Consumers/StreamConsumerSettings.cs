using System;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;
using Vostok.Metrics;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamConsumerSettings
    {
        public StreamConsumerSettings(
            [NotNull] string streamName,
            [NotNull] IHerculesStreamClient streamClient,
            [NotNull] IStreamEventsHandler eventsHandler,
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
        public IHerculesStreamClient StreamClient { get; }

        [NotNull]
        public IStreamEventsHandler EventsHandler { get; }

        [NotNull]
        public IStreamCoordinatesStorage CoordinatesStorage { get; }

        [NotNull]
        public Func<StreamShardingSettings> ShardingSettingsProvider { get; }

        [CanBeNull]
        public IMetricContext MetricContext { get; set; }

        public bool HandleWithoutEvents { get; set; }

        public int EventsReadBatchSize { get; set; } = ConsumersConstants.EventsReadBatchSize;

        public TimeSpan EventsReadTimeout { get; set; } = ConsumersConstants.EventsReadTimeout;

        public int EventsReadAttempts { get; set; } = ConsumersConstants.EventsReadAttempts;

        public TimeSpan DelayOnError { get; set; } = ConsumersConstants.DelayOnError;

        public TimeSpan DelayOnNoEvents { get; set; } = ConsumersConstants.DelayOnNoEvents;
    }
}
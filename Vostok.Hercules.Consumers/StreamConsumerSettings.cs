using System;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;

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

        public int EventsBatchSize { get; set; } = 10000;

        public TimeSpan EventsReadTimeout { get; set; } = TimeSpan.FromSeconds(45);

        public TimeSpan DelayOnError { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan DelayOnNoEvents { get; set; } = TimeSpan.FromSeconds(2);
    }
}

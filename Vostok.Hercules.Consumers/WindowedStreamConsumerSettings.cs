using System;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Commons.Time;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Metrics;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class WindowedStreamConsumerSettings<T, TKey>
    {
        public WindowedStreamConsumerSettings(
            [NotNull] string streamName,
            [NotNull] Func<string> apiKeyProvider,
            [NotNull] IClusterProvider streamApiCluster,
            [NotNull] Func<T, TKey> keyProvider,
            [NotNull] Func<T, DateTimeOffset> timestampProvider,
            [NotNull] Func<TKey, IWindow> createWindow,
            [NotNull] Func<IBinaryBufferReader, IHerculesEventBuilder<T>> eventBuilderProvider,
            [NotNull] IStreamCoordinatesStorage leftCoordinatesStorage,
            [NotNull] IStreamCoordinatesStorage rightCoordinatesStorage,
            [NotNull] Func<StreamShardingSettings> shardingSettingsProvider)
        {
            StreamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
            ApiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
            StreamApiCluster = streamApiCluster ?? throw new ArgumentNullException(nameof(streamApiCluster));
            KeyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            TimestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
            CreateWindow = createWindow ?? throw new ArgumentNullException(nameof(createWindow));
            EventBuilderProvider = eventBuilderProvider ?? throw new ArgumentNullException(nameof(eventBuilderProvider));
            LeftCoordinatesStorage = leftCoordinatesStorage ?? throw new ArgumentNullException(nameof(leftCoordinatesStorage));
            RightCoordinatesStorage = rightCoordinatesStorage ?? throw new ArgumentNullException(nameof(rightCoordinatesStorage));
            ShardingSettingsProvider = shardingSettingsProvider ?? throw new ArgumentNullException(nameof(shardingSettingsProvider));
        }

        [NotNull]
        public string StreamName { get; }

        [NotNull]
        public Func<string> ApiKeyProvider { get; }

        [NotNull]
        public IClusterProvider StreamApiCluster { get; }

        [NotNull]
        public Func<T, TKey> KeyProvider { get; }

        [NotNull]
        public Func<T, DateTimeOffset> TimestampProvider { get; }

        [NotNull]
        public Func<TKey, IWindow> CreateWindow { get; }

        [NotNull]
        public Func<IBinaryBufferReader, IHerculesEventBuilder<T>> EventBuilderProvider { get; }

        [NotNull]
        public IStreamCoordinatesStorage LeftCoordinatesStorage { get; }

        [NotNull]
        public IStreamCoordinatesStorage RightCoordinatesStorage { get; }

        [NotNull]
        public Func<StreamShardingSettings> ShardingSettingsProvider { get; }

        [CanBeNull]
        public ClusterClientSetup StreamApiClientAdditionalSetup { get; set; }

        [CanBeNull]
        public IMetricContext MetricContext { get; set; }

        public int EventsReadBatchSize { get; set; } = ConsumersConstants.EventsReadBatchSize;

        public TimeSpan EventsReadTimeout { get; set; } = ConsumersConstants.EventsReadTimeout;

        public TimeSpan DelayOnError { get; set; } = ConsumersConstants.DelayOnError;

        public TimeSpan DelayOnNoEvents { get; set; } = ConsumersConstants.DelayOnNoEvents;

        public int MaxPooledBufferSize { get; set; } = ConsumersConstants.MaxPooledBufferSize;

        public int MaxPooledBuffersPerBucket { get; set; } = ConsumersConstants.MaxPooledBuffersPerBucket;

        public TimeSpan DefaultPeriod { get; set; } = 1.Minutes();

        public TimeSpan DefaultLag { get; set; } = 30.Seconds();

        public TimeSpan MaximumEventBeforeNow { get; set; } = 1.Days();

        public TimeSpan MaximumEventAfterNow { get; set; } = 1.Minutes();

        public TimeSpan WindowsTtl { get; set; } = 1.Hours();

        public interface IWindow
        {
            void Add([NotNull] T @event);

            void Flush(DateTimeOffset timestamp);
        }
    }
}
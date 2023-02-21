using System;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Metrics;
using Vostok.Tracing.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class BatchesStreamConsumerSettings<T>
    {
        public BatchesStreamConsumerSettings(
            [NotNull] string streamName,
            [NotNull] Func<string> apiKeyProvider,
            [NotNull] IClusterProvider streamApiCluster,
            [NotNull] Func<IBinaryEventsReader, IHerculesEventBuilder<T>> eventBuilderProvider,
            [NotNull] IStreamCoordinatesStorage coordinatesStorage,
            [NotNull] Func<StreamShardingSettings> shardingSettingsProvider)
        {
            StreamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
            ApiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
            StreamApiCluster = streamApiCluster ?? throw new ArgumentNullException(nameof(streamApiCluster));
            EventBuilderProvider = eventBuilderProvider ?? throw new ArgumentNullException(nameof(eventBuilderProvider));
            CoordinatesStorage = coordinatesStorage ?? throw new ArgumentNullException(nameof(coordinatesStorage));
            ShardingSettingsProvider = shardingSettingsProvider ?? throw new ArgumentNullException(nameof(shardingSettingsProvider));
        }

        [NotNull]
        public string StreamName { get; }
        
        [CanBeNull]
        public string KafkaBootstrapServers { get; set; }
        
        [CanBeNull]
        public string ConsumerGroupId { get; set; }

        [NotNull]
        public Func<string> ApiKeyProvider { get; }

        [NotNull]
        public IClusterProvider StreamApiCluster { get; }

        [CanBeNull]
        public Action<T, StreamCoordinates> OnEvent { get; set; }

        [CanBeNull]
        public Action<StreamCoordinates> OnBatchBegin { get; set; }

        [CanBeNull]
        public Action<StreamCoordinates> OnBatchEnd { get; set; }

        [CanBeNull]
        public Action<StreamCoordinates> OnRestart { get; set; }
        
        [CanBeNull]
        public Action<StreamCoordinates> OnStop { get; set; }

        [NotNull]
        public Func<IBinaryEventsReader, IHerculesEventBuilder<T>> EventBuilderProvider { get; }

        [NotNull]
        public IStreamCoordinatesStorage CoordinatesStorage { get; }

        [NotNull]
        public Func<StreamShardingSettings> ShardingSettingsProvider { get; }

        [CanBeNull]
        public ClusterClientSetup StreamApiClientAdditionalSetup { get; set; }

        [CanBeNull]
        public IMetricContext ApplicationMetricContext { get; set; }

        [CanBeNull]
        public IMetricContext InstanceMetricContext { get; set; }

        [CanBeNull]
        public ITracer Tracer { get; set; }

        public int EventsReadBatchSize { get; set; } = ConsumersConstants.EventsReadBatchSize;

        public TimeSpan EventsReadTimeout { get; set; } = ConsumersConstants.EventsReadTimeout;

        public TimeSpan DelayOnError { get; set; } = ConsumersConstants.DelayOnError;

        public TimeSpan DelayOnNoEvents { get; set; } = ConsumersConstants.DelayOnNoEvents;

        public int MaxPooledBufferSize { get; set; } = ConsumersConstants.MaxPooledBufferSize;

        public int MaxPooledBuffersPerBucket { get; set; } = ConsumersConstants.MaxPooledBuffersPerBucket;
    }
}
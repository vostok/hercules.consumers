using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Metrics;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamTransformerSettings
    {
        public StreamTransformerSettings(
            [NotNull] string sourceStreamName,
            [NotNull] string targetStreamName,
            [NotNull] IHerculesStreamClient streamClient,
            [NotNull] IHerculesGateClient gateClient,
            [NotNull] IStreamCoordinatesStorage coordinatesStorage,
            [NotNull] Func<StreamShardingSettings> shardingSettingsProvider)
        {
            SourceStreamName = sourceStreamName ?? throw new ArgumentNullException(nameof(sourceStreamName));
            TargetStreamName = targetStreamName ?? throw new ArgumentNullException(nameof(targetStreamName));
            StreamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
            GateClient = gateClient ?? throw new ArgumentNullException(nameof(gateClient));
            CoordinatesStorage = coordinatesStorage ?? throw new ArgumentNullException(nameof(coordinatesStorage));
            ShardingSettingsProvider = shardingSettingsProvider ?? throw new ArgumentNullException(nameof(shardingSettingsProvider));
        }

        [NotNull]
        public string SourceStreamName { get; }

        [NotNull]
        public string TargetStreamName { get; }

        [NotNull]
        public IHerculesStreamClient StreamClient { get; }

        [NotNull]
        public IHerculesGateClient GateClient { get; }

        [NotNull]
        public IStreamCoordinatesStorage CoordinatesStorage { get; }

        [NotNull]
        public Func<StreamShardingSettings> ShardingSettingsProvider { get; }

        [CanBeNull]
        public Func<HerculesEvent, bool> Filter { get; set; }

        [CanBeNull]
        public Func<HerculesEvent, IEnumerable<HerculesEvent>> Transformer { get; set; }

        [CanBeNull]
        public IMetricContext MetricContext { get; set; }

        public int EventsReadBatchSize { get; set; } = ConsumersConstants.EventsReadBatchSize;

        public int EventsWriteBatchSize { get; set; } = ConsumersConstants.EventsWriteBatchSize;

        public TimeSpan EventsReadTimeout { get; set; } = ConsumersConstants.EventsReadTimeout;

        public TimeSpan EventsWriteTimeout { get; set; } = ConsumersConstants.EventsWriteTimeout;

        public int EventsReadAttempts { get; set; } = ConsumersConstants.EventsReadAttempts;

        public TimeSpan DelayOnError { get; set; } = ConsumersConstants.DelayOnError;

        public TimeSpan DelayOnNoEvents { get; set; } = ConsumersConstants.DelayOnNoEvents;
    }
}
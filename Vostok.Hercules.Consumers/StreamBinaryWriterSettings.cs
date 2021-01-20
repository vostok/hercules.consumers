using System;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Metrics;
using Vostok.Tracing.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamBinaryWriterSettings
    {
        public StreamBinaryWriterSettings(
            [NotNull] Func<string> apiKeyProvider,
            [NotNull] IClusterProvider gateCluster)
        {
            ApiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
            GateCluster = gateCluster ?? throw new ArgumentNullException(nameof(gateCluster));
        }

        [NotNull]
        public Func<string> ApiKeyProvider { get; }

        [NotNull]
        public IClusterProvider GateCluster { get; }

        [CanBeNull]
        public ClusterClientSetup GateClientAdditionalSetup { get; set; }

        [CanBeNull]
        public IMetricContext MetricContext { get; set; }

        [CanBeNull]
        public ITracer Tracer { get; set; }

        public int MaxPooledBufferSize { get; set; } = ConsumersConstants.MaxPooledBufferSize;

        public int MaxPooledBuffersPerBucket { get; set; } = ConsumersConstants.MaxPooledBuffersPerBucket;

        public TimeSpan EventsWriteTimeout { get; set; } = ConsumersConstants.EventsWriteTimeout;

        public TimeSpan DelayOnError { get; set; } = ConsumersConstants.DelayOnError;
    }
}
#if NET6_0_OR_GREATER

using System;
using JetBrains.Annotations;
using Vostok.Commons.Time;
using Vostok.Configuration.Primitives;
using Vostok.Metrics;
using Vostok.Tracing.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamBinaryEventsWriterSettings
    {
        public StreamBinaryEventsWriterSettings(StreamBinaryWriter streamBinaryWriter, string streamName)
        {
            StreamBinaryWriter = streamBinaryWriter;
            StreamName = streamName;
        }

        public StreamBinaryWriter StreamBinaryWriter { get; }

        public string StreamName { get; }

        public IMetricContext MetricContext { get; set; } = new DevNullMetricContext();
        public ITracer Tracer { get; set; } = TracerProvider.Get();
        
        public DataSize WriterCapacity { get; set; } = 4.Megabytes();
        public int WritersPoolCapacity { get; set; } = 8;        
        public TimeSpan WriterMaxTtl { get; set; } = 5.Seconds();
        
        public int MaximumEventSize { get; set; } = 128 * 1024;
    }
}

#endif
using System;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamWriterSettings
    {
        public StreamWriterSettings([NotNull] string targetStreamName, [NotNull] IHerculesGateClient gateClient)
        {
            TargetStreamName = targetStreamName ?? throw new ArgumentNullException(nameof(targetStreamName));
            GateClient = gateClient ?? throw new ArgumentNullException(nameof(gateClient));
        }

        [NotNull]
        public string TargetStreamName { get; }

        [NotNull]
        public IHerculesGateClient GateClient { get; }

        public int EventsWriteBatchSize { get; set; } = ConsumersConstants.EventsWriteBatchSize;

        public TimeSpan EventsWriteTimeout { get; set; } = ConsumersConstants.EventsWriteTimeout;

        public TimeSpan DelayOnError { get; set; } = ConsumersConstants.DelayOnError;
    }
}
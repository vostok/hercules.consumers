using System;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamReaderSettings
    {
        public StreamReaderSettings([NotNull] string streamName, [NotNull] IHerculesStreamClient streamClient)
        {
            StreamName = streamName;
            StreamClient = streamClient;
        }

        [NotNull]
        public string StreamName { get; }

        [NotNull]
        public IHerculesStreamClient StreamClient { get; }

        public int EventsReadBatchSize { get; set; } = ConsumersConstants.EventsReadBatchSize;

        public int EventsReadAttempts { get; set; } = ConsumersConstants.EventsReadAttempts;

        public TimeSpan EventsReadTimeout { get; set; } = ConsumersConstants.EventsReadTimeout;

        public TimeSpan DelayOnError { get; set; } = ConsumersConstants.DelayOnError;
    }
}
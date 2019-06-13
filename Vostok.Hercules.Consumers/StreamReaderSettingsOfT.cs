using System;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamReaderSettings<T>
    {
        public StreamReaderSettings([NotNull] string streamName, [NotNull] IHerculesStreamClient<T> streamClient)
        {
            StreamName = streamName;
            StreamClient = streamClient;
        }

        [NotNull]
        public string StreamName { get; }

        [NotNull]
        public IHerculesStreamClient<T> StreamClient { get; }

        public int EventsBatchSize { get; set; } = 10000;

        public TimeSpan EventsReadTimeout { get; set; } = TimeSpan.FromSeconds(45);
    }
}
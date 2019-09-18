using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamSegmentReaderSettings<T>
    {
        public StreamSegmentReaderSettings(
            [NotNull] string streamName,
            [NotNull] IHerculesStreamClient<T> streamClient,
            [NotNull] StreamCoordinates start,
            [NotNull] StreamCoordinates end)
        {
            StreamName = streamName;
            StreamClient = streamClient;
            Start = start.ToDictionary();
            End = end.ToDictionary();
        }

        [NotNull]
        public string StreamName { get; }

        [NotNull]
        public IHerculesStreamClient<T> StreamClient { get; }

        [NotNull]
        public Dictionary<int, StreamPosition> Start { get; }

        [NotNull]
        public Dictionary<int, StreamPosition> End { get; }

        public int EventsBatchSize { get; set; } = 10000;

        public TimeSpan EventsReadTimeout { get; set; } = TimeSpan.FromSeconds(45);
    }
}
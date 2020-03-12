using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;
using Vostok.Hercules.Client.Abstractions.Events;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamReaderSettings : StreamReaderSettings<HerculesEvent>
    {
        public StreamReaderSettings([NotNull] string streamName, [NotNull] IHerculesStreamClient streamClient)
            : base(streamName, streamClient.ToGenericClient())
        {
        }
    }
}
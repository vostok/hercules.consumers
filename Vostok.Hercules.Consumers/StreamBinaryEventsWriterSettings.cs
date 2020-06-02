using JetBrains.Annotations;

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

        public int BufferCapacityLimit { get; set; } = 128 * 1024 * 1024;
    }
}
using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Binary;
using Vostok.Commons.Time;
using Vostok.Hercules.Client;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Serialization.Builders;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Context;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamBinaryEventsWriter
    {
        private readonly StreamBinaryEventsWriterSettings settings;
        private readonly ILog log;
        private readonly BinaryBufferWriter buffer;
        private int eventsCount;
        private int eventsDropped;

        public StreamBinaryEventsWriter([NotNull] StreamBinaryEventsWriterSettings settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log = (log ?? LogProvider.Get()).ForContext<StreamBinaryEventsWriter>();

            buffer = new BinaryBufferWriter(0) {Endianness = Endianness.Big};
            buffer.Write(0);
        }

        public void Put(Action<IHerculesEventBuilder> buildEvent)
        {
            if (buffer.Length > settings.BufferCapacityLimit)
            {
                eventsDropped++;
                return;
            }

            eventsCount++;

            using (var eventBuilder = new BinaryEventBuilder(buffer, () => PreciseDateTime.UtcNow, Constants.EventProtocolVersion))
            {
                buildEvent(eventBuilder);
            }
        }

        public async Task WriteAsync()
        {
            LogDroppedEvents();

            using (buffer.JumpTo(0))
            {
                buffer.Write(eventsCount);
            }

            await settings.StreamBinaryWriter.WriteAsync(settings.StreamName, buffer.FilledSegment, eventsCount).ConfigureAwait(false);

            buffer.Reset();
            buffer.Write(0);
            eventsCount = 0;
        }

        private void LogDroppedEvents()
        {
            using (new OperationContextToken("WriteEvents"))
            {
                if (eventsDropped > 0)
                {
                    log.Error("Events dropped due to buffer overflow: {EventsCount}. Buffer capacity: {Capacity}.", eventsDropped, settings.BufferCapacityLimit);
                    eventsDropped = 0;
                }
            }
        }
    }
}
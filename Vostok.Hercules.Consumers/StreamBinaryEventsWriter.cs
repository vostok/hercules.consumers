#if NET6_0_OR_GREATER

using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Binary;
using Vostok.Commons.Time;
using Vostok.Hercules.Client;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Serialization.Builders;
using Vostok.Logging.Abstractions;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Timer;
using Vostok.Tracing.Extensions.Custom;

// ReSharper disable ParameterHidesMember

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamBinaryEventsWriter
    {
        private readonly StreamBinaryEventsWriterSettings settings;
        private readonly ILog log;

        private readonly Channel<Writer> emptyWriters;
        private readonly IMetricGroup1<ITimer> iterationMetric;

        private Writer writer;

        public StreamBinaryEventsWriter(StreamBinaryEventsWriterSettings settings, ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = (log ?? LogProvider.Get()).ForContext<StreamBinaryEventsWriter>();

            iterationMetric = settings.MetricContext.CreateSummary("iteration", "type", new SummaryConfig {Quantiles = new[] {0.5, 0.75, 1}});

            emptyWriters = Channel.CreateUnbounded<Writer>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            for (var i = 0; i < settings.WritersPoolCapacity; i++)
                emptyWriters.Writer.WriteAsync(new Writer(settings.WriterCapacity.Bytes)).AsTask().GetAwaiter().GetResult();
        }

        public void Put(Action<IHerculesEventBuilder> buildEvent)
        {
            if (writer == null || writer.RemainingCapacity < settings.MaximumEventSize || DateTime.UtcNow - writer.WritingStarted > settings.WriterMaxTtl)
            {
                if (writer != null)
                {
                    var fullWriter = writer;
                    Task.Run(() => FlushWriter(fullWriter));
                }

                writer = ObtainEmptyWriter();
            }

            writer.StartEvent();

            using var eventBuilder = new BinaryEventBuilder(writer, () => PreciseDateTime.UtcNow, Constants.EventProtocolVersion);

            buildEvent(eventBuilder);
        }

        // note (kungurtsev, 15.08.2022): do not call concurrently with Put
        public async Task FlushAsync()
        {
            if (writer is null)
                return;

            await FlushWriter(writer);
            writer = ObtainEmptyWriter();
        }

        private Writer ObtainEmptyWriter()
        {
            using var _ = settings.Tracer.BeginCustomOperationSpan("WaitWriter");
            using var __ = iterationMetric.For("waitWriter_time").Measure();

            return emptyWriters.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
        }

        private async Task FlushWriter(Writer writer)
        {
            writer.EndWriting();

            await settings.StreamBinaryWriter.WriteAsync(settings.StreamName, writer.FilledSegment, writer.EventsCount).ConfigureAwait(false);

            writer.Reset();
            
            await emptyWriters.Writer.WriteAsync(writer);
        }

        private class Writer : BinaryBufferWriter
        {
            public int EventsCount;
            public DateTime? WritingStarted;

            public Writer(long initialCapacity)
                : base((int)initialCapacity)
            {
                Endianness = Endianness.Big;
            }

            public long RemainingCapacity => Buffer.Length - Position;

            public void StartEvent()
            {
                if (EventsCount == 0)
                {
                    Write(0);
                    WritingStarted = DateTime.UtcNow;
                }

                EventsCount++;
            }

            public void EndWriting()
            {
                using (this.JumpTo(0))
                    Write(EventsCount);
            }

            public void Reset()
            {
                base.Reset();
                EventsCount = 0;
                WritingStarted = null;
            }
        }
    }
}

#endif
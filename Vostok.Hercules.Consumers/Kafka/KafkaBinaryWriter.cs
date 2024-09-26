using System;
using System.Collections.Generic;
using System.Threading;
using Confluent.Kafka;
using JetBrains.Annotations;
using Vostok.Logging.Abstractions;
using Vostok.Metrics;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Counter;

namespace Vostok.Hercules.Consumers.Kafka;

[PublicAPI]
public class KafkaBinaryWriter : IDisposable
{
    private readonly ILog log;
    private readonly ICounter writeEventsCounter;
    private readonly IMetricGroup2<ICounter> errorCounter;

    private readonly IProducer<Guid, byte[]> producer;
    private bool disposed = false;

    public KafkaBinaryWriter(ILog log, IMetricContext metricContext, IEnumerable<KeyValuePair<string, string>> producerConfig)
    {
        this.log = log?.ForContext<KafkaBinaryWriter>() ?? new SilentLog();

        metricContext = metricContext?.WithTag("HerculesComponent", "KafkaBinaryWriter") ?? new DevNullMetricContext();
        writeEventsCounter = metricContext.CreateCounter("writes_count");
        errorCounter = metricContext.CreateCounter("write_errors", "error_type", "error_level");

        var builder = new ProducerBuilder<Guid, byte[]>(producerConfig)
            .SetErrorHandler((_, error) => LogError(error))
            .SetKeySerializer(GuidSerializer.Instance);

#if NET6_0_OR_GREATER
        builder.SetStatisticsHandler(new KafkaProducerStatisticsHandler(metricContext).Write);
#endif

        producer = builder.Build();
    }

    public void Flush(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        producer.Flush(cancellationToken);
    }

    public void Write(string streamName, byte[] content)
    {
        ThrowIfDisposed();
        producer.Produce(streamName,
            new Message<Guid, byte[]>
            {
                Key = Guid.NewGuid(),
                Value = content
            });

        writeEventsCounter.Increment();
    }

    public void Dispose()
    {
        disposed = true;
        producer?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(KafkaBinaryWriter));
    }

    private void LogError(Error error)
    {
        if (error.IsFatal)
            log.Error($"Failed to write Kafka event ({error.Code}): {error.Reason}");
        else
            log.Warn($"Failed to write Kafka event ({error.Code}): {error.Reason}");

        errorCounter
            .For(error.Code.ToString("G"), error.IsFatal ? "Fatal" : "NonFatal")
            .Increment();
    }
}
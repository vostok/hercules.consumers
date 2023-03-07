using System;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;
using Vostok.Commons.Helpers.Disposable;
using Vostok.Commons.Time;
using Vostok.Configuration.Primitives;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Hercules.Client.Internal;
using Vostok.Logging.Abstractions;

namespace Vostok.Hercules.Consumers.Kafka;

internal sealed class KafkaTopicReader
{
    private readonly KafkaTopicReaderSettings settings;
    private readonly ILog log;
    private readonly BufferPool bufferPool;

    private readonly IConsumer<Ignore, byte[]> consumer;

    private static readonly DataSize ReadBufferRentSize = 25.Megabytes(); // Approximate size
    private static readonly TimeSpan ConsumeTimeout = 1.Seconds();
    private static readonly Partition Partition = new(0); // Because we running experiment on replica with index = 0

    public KafkaTopicReader(KafkaTopicReaderSettings settings, ILog log, BufferPool bufferPool)
    {
        this.settings = settings;
        this.log = log.ForContext<KafkaTopicReader>();
        this.bufferPool = bufferPool;

        var consumerConfig = new ConsumerConfig
        {
            GroupId = this.settings.GroupId,
            BootstrapServers = this.settings.BootstrapServers,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = false,
            FetchMinBytes = this.settings.FetchMinBytes,
            FetchWaitMaxMs = this.settings.FetchWaitMaxMs,
            MaxPartitionFetchBytes = 52428800
        };

        var builder = new ConsumerBuilder<Ignore, byte[]>(consumerConfig);
        consumer = builder.Build();
    }

    public void Assign()
    {
        consumer.Assign(new TopicPartition(settings.Topic, Partition));
        log.Info($"Kafka consumer assigned to topic. Fetch min bytes: {settings.FetchMinBytes}, Fetch wait max ms: {settings.FetchWaitMaxMs}".ToString());
    }

    public async Task<RawReadStreamResult> ReadAsync(ReadStreamQuery query) =>
        await Task.Run(() => ReadInternal(query)).ConfigureAwait(false);

    private RawReadStreamResult ReadInternal(ReadStreamQuery query)
    {
        var lastConsumedOffset = SeekBeforeRead(query.Coordinates);

        var eventsWriter = new BinaryBufferWriter(bufferPool.Rent((int)ReadBufferRentSize.Bytes))
        {
            Endianness = Endianness.Big,
            Position = sizeof(int) // For messages count
        };

        var eventsCount = 0;
        try
        {
            while (eventsCount < query.Limit)
            {
                var result = consumer.Consume(ConsumeTimeout);

                if (result?.Message is null)
                    break;

                eventsWriter.WriteWithoutLength(result.Message.Value);
                lastConsumedOffset = result.Offset;
                eventsCount++;
            }
        }
        catch (Exception e)
        {
            log.Warn(e, "Error while consuming kafka events");
            if (eventsCount == 0)
            {
                bufferPool.Return(eventsWriter.Buffer);
                return new RawReadStreamResult(HerculesStatus.UnknownError, null, e.Message);
            }
        }

        using (eventsWriter.JumpTo(0))
            eventsWriter.Write(eventsCount);

        var rawReadStreamPayload = new RawReadStreamPayload(
            new ValueDisposable<ArraySegment<byte>>(
                eventsWriter.FilledSegment,
                new ActionDisposable(() => bufferPool.Return(eventsWriter.Buffer))),
            new StreamCoordinates(new[]
            {
                new StreamPosition
                {
                    Partition = Partition,
                    Offset = lastConsumedOffset + 1
                }
            }));
        return new RawReadStreamResult(HerculesStatus.Success, rawReadStreamPayload);
    }

    private Offset SeekBeforeRead(StreamCoordinates coordinates)
    {
        var position = coordinates.Positions.First(p => p.Partition == Partition);
        consumer.Seek(new TopicPartitionOffset(settings.Topic,
            new Partition(position.Partition),
            new Offset(position.Offset)));

        var lastConsumedOffset = position.Offset - 1;

        return lastConsumedOffset;
    }

    public void Close()
    {
        consumer.Close();
        consumer.Dispose();

        log.Info("Kafka consumer stopped.");
    }
}
using System;
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
            EnableAutoCommit = false
        };

        var builder = new ConsumerBuilder<Ignore, byte[]>(consumerConfig);
        consumer = builder.Build();
    }

    public void Assign()
    {
        consumer.Assign(new TopicPartition(settings.Topic, Partition));
        log.Info("Kafka consumer assigned to coordinates");
    }

    public async Task<RawReadStreamResult> ReadAsync(ReadStreamQuery query, TimeSpan timeout) =>
        await Task.Run(() => ReadInternal(query, timeout)).ConfigureAwait(false);

    private RawReadStreamResult ReadInternal(ReadStreamQuery query, TimeSpan _)
    {
        SeekBeforeRead(query.Coordinates);

        var eventsWriter = new BinaryBufferWriter(bufferPool.Rent((int)ReadBufferRentSize.Bytes))
        {
            Endianness = Endianness.Big,
            Position = sizeof(int) // For messages count
        };

        var eventsCount = 0;
        var lastConsumedOffset = Offset.Unset;
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

    private void SeekBeforeRead(StreamCoordinates coordinates)
    {
        foreach (var position in coordinates.Positions)
        {
            consumer.Seek(new TopicPartitionOffset(settings.Topic,
                new Partition(position.Partition),
                new Offset(position.Offset)));
        }
    }

    public Task<SeekToEndStreamResult> SeekToEndAsync(SeekToEndStreamQuery _, TimeSpan __)
    {
        try
        {
            var streamPositions = new StreamPosition[consumer.Assignment.Count];
            for (var i = 0; i < consumer.Assignment.Count; i++)
            {
                var topicPartition = consumer.Assignment[i];
                var highOffset = consumer.GetWatermarkOffsets(topicPartition).High;

                if (highOffset == Offset.Unset)
                    return Task.FromResult(new SeekToEndStreamResult(HerculesStatus.UnknownError, null, "Found Unset offset"));

                streamPositions[i] = new StreamPosition
                {
                    Partition = topicPartition.Partition.Value,
                    Offset = highOffset.Value
                };
            }

            var seekToEndStreamPayload = new SeekToEndStreamPayload(new StreamCoordinates(streamPositions));
            return Task.FromResult(new SeekToEndStreamResult(HerculesStatus.Success, seekToEndStreamPayload));
        }
        catch (Exception e)
        {
            return Task.FromResult(new SeekToEndStreamResult(HerculesStatus.UnknownError, null, e.Message));
        }
    }

    public void Close()
    {
        consumer.Close();
        consumer.Dispose();

        log.Info("Kafka consumer stopped.");
    }
}
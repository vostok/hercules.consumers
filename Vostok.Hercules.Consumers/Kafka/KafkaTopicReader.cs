using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;
using Vostok.Commons.Helpers.Disposable;
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
    private static readonly Partition[] Partitions = {0, 99};

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
            FetchWaitMaxMs = this.settings.FetchWaitMaxMs
        };

        var builder = new ConsumerBuilder<Ignore, byte[]>(consumerConfig);
        consumer = builder.Build();
    }

    public void Assign()
    {
        consumer.Assign(Partitions.Select(partition => new TopicPartition(settings.Topic, partition)));

        log.Info($"Kafka consumer assigned to topic {settings.Topic} ({Partitions}). " +
                 $"Fetch min bytes: {settings.FetchMinBytes}, " +
                 $"Fetch wait max ms: {settings.FetchWaitMaxMs}, " +
                 $"Consume timeout ms: {settings.ConsumeTimeout.TotalMilliseconds}"
        );
    }

    public async Task<RawReadStreamResult> ReadAsync(ReadStreamQuery query) =>
        await Task.Run(() => ReadInternal(query)).ConfigureAwait(false);

    private RawReadStreamResult ReadInternal(ReadStreamQuery query)
    {
        var positions = SeekBeforeRead(query.Coordinates);

        var eventsWriter = new BinaryBufferWriter(bufferPool.Rent((int)ReadBufferRentSize.Bytes))
        {
            Endianness = Endianness.Big,
            Position = sizeof(int) // For messages count
        };

        var eventsCount = 0;
        try
        {
            // Суть костыля: с нормальным таймаутом читается только первое сообщение из пачки.
            // Остальные либо сразу достаются из буффера, либо сразу получаем null - индикатор конца пачки.
            var message = consumer.Consume(settings.ConsumeTimeout);
            if (message?.Message is null)
                throw new Exception();

            eventsWriter.WriteWithoutLength(message.Message.Value);
            positions[message.Partition] = message.Offset;
            eventsCount++;

            while (eventsCount < query.Limit)
            {
                message = consumer.Consume(TimeSpan.Zero); // TimeSpan.Zero
                if (message?.Message is null)
                    break;

                eventsWriter.WriteWithoutLength(message.Message.Value);
                positions[message.Partition] = message.Offset;
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
        {
            log.Info($"ReadInternal.eventsCount: {eventsCount}");
            eventsWriter.Write(eventsCount);
        }

        var rawReadStreamPayload = new RawReadStreamPayload(
            new ValueDisposable<ArraySegment<byte>>(
                eventsWriter.FilledSegment,
                new ActionDisposable(() => bufferPool.Return(eventsWriter.Buffer))),
            new StreamCoordinates(
                // offset is incremented because it will be used as inclusive start at next iteration
                positions
                    .Select(p => new StreamPosition {Partition = p.Key, Offset = p.Value + 1}) 
                    .ToArray()
            ));

        log.Info($"ReadInternal.rawReadStreamPayload.Next: {rawReadStreamPayload.Next}");
        return new RawReadStreamResult(HerculesStatus.Success, rawReadStreamPayload);
    }

    private Dictionary<Partition, Offset> SeekBeforeRead(StreamCoordinates coordinates)
    {
        var positions = coordinates.Positions
            .Where(pos => Partitions.Any(partition => partition.Value == pos.Partition))
            .ToDictionary(
                pos => new Partition(pos.Partition), 
                pos => new Offset(pos.Offset - 1)
            );

        log.Info($"SeekBeforeRead.positions: {positions}");
        foreach (var position in positions)
            consumer.Seek(new TopicPartitionOffset(settings.Topic, position.Key, position.Value));

        return positions;
    }

    public void Close()
    {
        consumer.Close();
        consumer.Dispose();

        log.Info("Kafka consumer stopped.");
    }
}
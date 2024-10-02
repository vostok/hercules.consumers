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

    private readonly IConsumer<Ignore, RentedBuffer> consumer;

    private static readonly DataSize ReadBufferRentSize = 25.Megabytes(); // Approximate size

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

        var builder = new ConsumerBuilder<Ignore, RentedBuffer>(consumerConfig)
            .SetValueDeserializer(new RentedBufferDeserializer(this.bufferPool));

        consumer = builder.Build();
    }

    public void Assign(StreamCoordinates coordinates)
    {
        consumer.Assign(coordinates.Positions.Select(pos => new TopicPartitionOffset(settings.Topic, pos.Partition, pos.Offset)));

        log.Info($"Kafka consumer assigned to topic {settings.Topic} ({coordinates}). " +
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
            if (!TryConsume(eventsWriter, positions, settings.ConsumeTimeout, ref eventsCount))
                throw new Exception();

            while (eventsCount < query.Limit)
            {
                if (!TryConsume(eventsWriter, positions, TimeSpan.Zero, ref eventsCount))
                    break;
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

    private bool TryConsume(
        BinaryBufferWriter eventsWriter,
        Dictionary<Partition, Offset> positions,
        TimeSpan timeout,
        ref int eventsCount)
    {
        var result = consumer.Consume(timeout);
        if (result?.Message is null)
            return false;

        try
        {
            eventsWriter.WriteWithoutLength(result.Message.Value.Bytes, 0, result.Message.Value.Length);
        }
        finally
        {
            result.Message.Value.Return();
        }

        positions[result.Partition] = result.Offset;
        eventsCount++;
        return true;
    }

    private Dictionary<Partition, Offset> SeekBeforeRead(StreamCoordinates coordinates)
    {
        var positions = coordinates.Positions
            .Where(pos => consumer.Assignment.Any(a => a.Partition == pos.Partition))
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
﻿using System;
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
    private static readonly Partition Partition = new(0);

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
        consumer.Assign(new TopicPartition(settings.Topic, Partition));
        log.Info($"Kafka consumer assigned to topic. Fetch min bytes: {settings.FetchMinBytes}, Fetch wait max ms: {settings.FetchWaitMaxMs}, Consume timeout ms: {settings.ConsumeTimeout.TotalMilliseconds}".ToString());
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
            // Суть костыля: с нормальным таймаутом читается только первое сообщение из пачки.
            // Остальные либо сразу достаются из буффера, либо сразу получаем null - индикатор конца пачки.
            var message = consumer.Consume(settings.ConsumeTimeout);
            if (message?.Message is null)
                throw new Exception();

            eventsWriter.WriteWithoutLength(message.Message.Value);
            lastConsumedOffset = message.Offset;
            eventsCount++;

            while (eventsCount < query.Limit)
            {
                message = consumer.Consume(settings.ConsumeTimeout); // TimeSpan.Zero
                if (message?.Message is null)
                    break;

                eventsWriter.WriteWithoutLength(message.Message.Value);
                lastConsumedOffset = message.Offset;
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
            new StreamCoordinates(new[]
            {
                new StreamPosition
                {
                    Partition = Partition,
                    Offset = lastConsumedOffset + 1 // ?
                }
            }));
        log.Info($"ReadInternal.rawReadStreamPayload.Next: {rawReadStreamPayload.Next}");
        return new RawReadStreamResult(HerculesStatus.Success, rawReadStreamPayload);
    }

    private Offset SeekBeforeRead(StreamCoordinates coordinates)
    {
        var position = coordinates.Positions.First(p => p.Partition == Partition);
        log.Info($"SeekBeforeRead.position: {position}");
        consumer.Seek(new TopicPartitionOffset(settings.Topic,
            new Partition(position.Partition),
            new Offset(position.Offset)));

        var lastConsumedOffset = position.Offset - 1; // ?

        log.Info($"SeekBeforeRead.lastConsumedOffset: {lastConsumedOffset}");
        return lastConsumedOffset;
    }

    public void Close()
    {
        consumer.Close();
        consumer.Dispose();

        log.Info("Kafka consumer stopped.");
    }
}
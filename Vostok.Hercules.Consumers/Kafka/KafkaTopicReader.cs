using System;
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
        
        var poolingValueDeserializer = new PoolingValueDeserializer(this);
        var builder = new ConsumerBuilder<Ignore, byte[]>(consumerConfig).SetValueDeserializer(poolingValueDeserializer);
        consumer = builder.Build();
    }

    public void Assign(StreamCoordinates coordinates)
    {
        var topicPartitionOffsets = coordinates.Positions.Select(position =>
            new TopicPartitionOffset(settings.Topic, new Partition(position.Partition), new Offset(position.Offset)));

        consumer.Assign(topicPartitionOffsets);

        log.Info("Kafka consumer assigned to coordinates {StreamCoordinates}", coordinates);
    }

    public async Task<RawReadStreamResult> ReadAsync(ReadStreamQuery query, TimeSpan timeout) =>
        await Task.Run(() => Read(query, timeout));

    private RawReadStreamResult Read(ReadStreamQuery query, TimeSpan timeout)
    {
        var binaryWriter = new BinaryBufferWriter((int)4.Megabytes().Bytes)
        {
            Endianness = Endianness.Big
        };

        var count = 0;
        var isEof = false;
        while (count <= query.Limit && !isEof)
        {
            var result = consumer.Consume();
            
            binaryWriter.WriteWithoutLength(result.Message.Value);
            count++;

            isEof = result.IsPartitionEOF;
        }

        var nextCoordinates = StreamCoordinates.Empty;
        
        var valueDisposable = new ValueDisposable<ArraySegment<byte>>(binaryWriter.FilledSegment, new EmptyDisposable());
        var rawReadStreamPayload = new RawReadStreamPayload(valueDisposable, nextCoordinates);
        return new RawReadStreamResult(HerculesStatus.Success, rawReadStreamPayload);
    }

    public Task<SeekToEndStreamResult> SeekToEndAsync(SeekToEndStreamQuery _, TimeSpan __)
    {
        var streamPositions = new StreamPosition[consumer.Assignment.Count];
        for (var i = 0; i < consumer.Assignment.Count; i++)
        {
            var topicPartition = consumer.Assignment[i];
            var watermarkOffsets = consumer.GetWatermarkOffsets(topicPartition);

            streamPositions[i] = new StreamPosition
            {
                Partition = topicPartition.Partition.Value,
                Offset = watermarkOffsets.High.Value
            };
        }

        var seekToEndStreamPayload = new SeekToEndStreamPayload(new StreamCoordinates(streamPositions));
        var result = new SeekToEndStreamResult(HerculesStatus.Success, seekToEndStreamPayload);

        return Task.FromResult(result);
    }

    public void Close()
    {
        consumer.Close();
        consumer.Dispose();
        
        log.Info("Kafka consumer stopped.");
    }
    
    private sealed class PoolingValueDeserializer : IDeserializer<byte[]>
    {
        private readonly KafkaTopicReader topicReader;

        public PoolingValueDeserializer(KafkaTopicReader topicReader)
        {
            this.topicReader = topicReader;
        }

        public byte[] Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            var bytes = topicReader.bufferPool.Rent(data.Length);
            data.CopyTo(bytes);
            
            return bytes;
        }
    }
}
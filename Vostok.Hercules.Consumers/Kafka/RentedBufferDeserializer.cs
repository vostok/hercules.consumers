using System;
using Confluent.Kafka;
using Vostok.Commons.Collections;

namespace Vostok.Hercules.Consumers.Kafka;

internal class RentedBufferDeserializer : IDeserializer<RentedBuffer>
{
    private readonly BufferPool pool;

    public RentedBufferDeserializer(BufferPool pool) =>
        this.pool = pool;

    public RentedBuffer Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
    {
        if (data.Length == 0)
            return RentedBuffer.Empty;

        var buffer = pool.Rent(data.Length);
        data.CopyTo(buffer);

        return new RentedBuffer(pool, buffer, data.Length);
    }
}
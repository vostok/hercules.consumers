using System;
using Vostok.Commons.Collections;

namespace Vostok.Hercules.Consumers.Kafka;

internal readonly struct RentedBuffer
{
    private readonly BufferPool pool;
    public readonly byte[] Bytes;
    public readonly int Length;

    public RentedBuffer(BufferPool pool, byte[] value, int length)
    {
        this.pool = pool;
        Length = length;
        Bytes = value;
    }

    public static RentedBuffer Empty =>
        new(null, Array.Empty<byte>(), 0);

    public void Return() =>
        pool?.Return(Bytes);
}
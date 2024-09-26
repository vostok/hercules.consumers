using System;
using Confluent.Kafka;

namespace Vostok.Hercules.Consumers.Kafka;

internal class GuidSerializer : ISerializer<Guid>
{
    public static readonly GuidSerializer Instance = new();

    public byte[] Serialize(Guid data, SerializationContext context) =>
        data.ToByteArray();
}
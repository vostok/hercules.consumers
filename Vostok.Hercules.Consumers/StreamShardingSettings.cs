using System;
using JetBrains.Annotations;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamShardingSettings
    {
        public StreamShardingSettings(int clientShardIndex, int clientShardCount)
        {
            if (clientShardCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(clientShardCount), "Client shard count must be > 0.");

            if (clientShardIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(clientShardIndex), "Client shard index must be >= 0.");

            if (clientShardIndex >= clientShardCount)
                throw new ArgumentOutOfRangeException(nameof(clientShardIndex), "Client shard index must be less than shard count.");

            ClientShardIndex = clientShardIndex;
            ClientShardCount = clientShardCount;
        }

        public int ClientShardIndex { get; }

        public int ClientShardCount { get; }
    }
}
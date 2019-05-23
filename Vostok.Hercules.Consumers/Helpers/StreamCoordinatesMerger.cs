using System;
using System.Linq;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class StreamCoordinatesMerger
    {
        [NotNull]
        public static StreamCoordinates Merge([NotNull] StreamCoordinates left, [NotNull] StreamCoordinates right)
        {
            var map = left.ToDictionary();

            foreach (var position in right.Positions)
            {
                if (!map.TryGetValue(position.Partition, out var currentPosition))
                {
                    map[position.Partition] = position;
                }
                else
                {
                    map[position.Partition] = new StreamPosition
                    {
                        Partition = position.Partition,
                        Offset = Math.Max(position.Offset, currentPosition.Offset)
                    };
                }
            }

            return new StreamCoordinates(map.Values.ToArray());
        }
    }
}
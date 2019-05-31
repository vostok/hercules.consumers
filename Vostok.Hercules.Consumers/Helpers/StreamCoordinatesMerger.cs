using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Add zeros to left, filter by right partitions.
        /// </summary>
        public static StreamCoordinates FixInitialCoordinates([NotNull] StreamCoordinates left, [NotNull] StreamCoordinates right)
        {
            var map = left.ToDictionary();
            var result = new List<StreamPosition>();

            foreach (var position in right.Positions)
            {
                if (!map.TryGetValue(position.Partition, out var inital))
                {
                    result.Add(new StreamPosition
                    {
                        Offset = 0,
                        Partition = position.Partition
                    });
                }
                else
                {
                    result.Add(inital);
                }
            }

            return new StreamCoordinates(result.ToArray());
        }
    }
}
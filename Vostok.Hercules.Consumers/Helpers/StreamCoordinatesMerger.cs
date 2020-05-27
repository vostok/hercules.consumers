using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    [PublicAPI]
    public static class StreamCoordinatesMerger
    {
        [NotNull]
        public static StreamCoordinates MergeMaxWith([NotNull] this StreamCoordinates left, [NotNull] StreamCoordinates right)
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

        [NotNull]
        public static StreamCoordinates MergeMinWith([NotNull] this StreamCoordinates left, [NotNull] StreamCoordinates right)
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
                        Offset = Math.Min(position.Offset, currentPosition.Offset)
                    };
                }
            }

            return new StreamCoordinates(map.Values.ToArray());
        }

        /// <summary>
        /// Filter by next partitions.
        /// </summary>
        public static StreamCoordinates FilterBy([NotNull] this StreamCoordinates left, [NotNull] StreamCoordinates right)
        {
            var map = left.ToDictionary();
            var result = new List<StreamPosition>();

            foreach (var position in right.Positions)
            {
                if (map.TryGetValue(position.Partition, out var was))
                    result.Add(was);
            }

            return new StreamCoordinates(result.ToArray());
        }

        public static long DistanceTo([NotNull] this StreamCoordinates from, [NotNull] StreamCoordinates to)
        {
            var map = from.ToDictionary();
            var result = 0L;

            foreach (var position in to.Positions)
            {
                if (map.TryGetValue(position.Partition, out var p))
                {
                    result += position.Offset - p.Offset;
                }
                else
                {
                    result += position.Offset;
                }
            }

            return result;
        }
    }
}
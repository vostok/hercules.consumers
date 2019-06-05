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
        public static StreamCoordinates MergeMax([NotNull] StreamCoordinates left, [NotNull] StreamCoordinates right)
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
        public static StreamCoordinates MergeMin([NotNull] StreamCoordinates left, [NotNull] StreamCoordinates right)
        {
            var map = left.ToDictionary();
            var result = new List<StreamPosition>();

            foreach (var position in right.Positions)
            {
                if (map.TryGetValue(position.Partition, out var p))
                {
                    result.Add(
                        new StreamPosition
                        {
                            Offset = Math.Min(position.Offset, p.Offset),
                            Partition = position.Partition
                        });
                }
            }

            return new StreamCoordinates(result.ToArray());
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
                    result.Add(
                        new StreamPosition
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

        public static long Distance([NotNull] StreamCoordinates from, [NotNull] StreamCoordinates to)
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
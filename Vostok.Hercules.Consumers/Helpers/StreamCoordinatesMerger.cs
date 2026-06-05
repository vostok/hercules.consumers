using System;
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
            var result = CoordinatesUtils
                .Zip(left, right)
                .Select(x => new StreamPosition
                {
                    Partition = x.Partition,
                    Offset = (x.LeftOffset, x.RightOffset) switch
                    {
                        (null, not null) => x.RightOffset.Value,
                        (not null, null) => x.LeftOffset.Value,
                        (not null, not null) => Math.Max(x.LeftOffset.Value, x.RightOffset.Value)
                    }
                })
                .ToArray();

            return new StreamCoordinates(result);
        }

        [NotNull]
        public static StreamCoordinates MergeMinWith([NotNull] this StreamCoordinates left, [NotNull] StreamCoordinates right)
        {
            var result = CoordinatesUtils
                .Zip(left, right)
                .Select(x => new StreamPosition
                {
                    Partition = x.Partition,
                    Offset = (x.LeftOffset, x.RightOffset) switch
                    {
                        (null, not null) => x.RightOffset.Value,
                        (not null, null) => x.LeftOffset.Value,
                        (not null, not null) => Math.Min(x.LeftOffset.Value, x.RightOffset.Value)
                    }
                })
                .ToArray();

            return new StreamCoordinates(result);
        }

        /// <summary>
        /// Filter by next partitions.
        /// </summary>
        public static StreamCoordinates FilterBy([NotNull] this StreamCoordinates left, [NotNull] StreamCoordinates right)
        {
            var result = CoordinatesUtils
                .Zip(left, right)
                .Where(x => x.LeftOffset != null && x.RightOffset != null)
                .Select(x => new StreamPosition
                {
                    Partition = x.Partition,
                    Offset = x.LeftOffset.Value
                })
                .ToArray();

            return StreamCoordinates.CreateUnsafe(result);
        }

        public static long DistanceTo([NotNull] this StreamCoordinates from, [NotNull] StreamCoordinates to)
        {
            var result = 0L;
            foreach (var (_, fromOffset, toOffset) in CoordinatesUtils.Zip(from, to))
            {
                if (toOffset is null)
                    continue;

                result += toOffset.Value - (fromOffset ?? 0);
            }

            return result;
        }
    }
}
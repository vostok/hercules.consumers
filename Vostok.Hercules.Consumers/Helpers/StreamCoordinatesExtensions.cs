using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    [PublicAPI]
    public static class StreamCoordinatesExtensions
    {
        public static bool AdvancesOver([NotNull] this StreamCoordinates self, [NotNull] StreamCoordinates other)
        {
            foreach (var (_, selfOffset, otherOffset) in CoordinatesUtils.Zip(self, other))
            {
                if (otherOffset is null || selfOffset > otherOffset)
                    return true;
            }

            return false;
        }

        [Pure]
        public static StreamCoordinates SetPosition(this StreamCoordinates coordinates, StreamPosition position)
        {
            if (TryOverwriteExisting(coordinates, position, out var overwritten))
                return overwritten;

            var result = new StreamPosition[coordinates.Positions.Length + 1];
            var insertIndex = 0;

            foreach (var sourcePosition in coordinates.Positions)
            {
                if (sourcePosition.Partition >= position.Partition)
                    result[insertIndex++] = position;

                result[insertIndex++] = sourcePosition;
            }

            return StreamCoordinates.CreateUnsafe(result);
        }

        private static bool TryOverwriteExisting(
            StreamCoordinates coordinates,
            StreamPosition position,
            out StreamCoordinates result
        )
        {
            var index = Array.BinarySearch(coordinates.Positions, position, ByPositionStreamPositionComparer.Instance);
            if (index <= 0)
            {
                result = default;
                return false;
            }

            var positionsCopy = coordinates.Positions.ToArray();
            positionsCopy[index].Offset = position.Offset;
            result = StreamCoordinates.CreateUnsafe(positionsCopy);

            return true;
        }

        private sealed class ByPositionStreamPositionComparer : IComparer<StreamPosition>
        {
            public static readonly ByPositionStreamPositionComparer Instance = new ByPositionStreamPositionComparer();

            public int Compare(StreamPosition x, StreamPosition y) =>
                x.Partition.CompareTo(y.Partition);
        }
    }
}
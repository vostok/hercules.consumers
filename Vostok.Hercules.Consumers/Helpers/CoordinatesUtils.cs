using System.Collections.Generic;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers;

internal static class CoordinatesUtils
{
    public static IEnumerable<(int Partition, long? LeftOffset, long? RightOffset)> Zip(
        StreamCoordinates first,
        StreamCoordinates second
    )
    {
        var index1 = 0;
        var index2 = 0;

        while (TryReadMerged(first, second, ref index1, ref index2, out var partition, out var offset1, out var offset2))
            yield return (partition, offset1, offset2);

        while (first.TryGetElement(index1++, out var pos1))
            yield return (pos1.Partition, pos1.Offset, null);

        while (second.TryGetElement(index2++, out var pos2))
            yield return (pos2.Partition, null, pos2.Offset);
    }

    private static bool TryReadMerged(
        StreamCoordinates first,
        StreamCoordinates second,
        ref int firstIndex,
        ref int secondIndex,
        out int partition,
        out long? firstOffset,
        out long? secondOffset
    )
    {
        if (!first.TryGetElement(firstIndex, out var firstPos) || !second.TryGetElement(secondIndex, out var secondPos))
        {
            partition = 0;
            firstOffset = null;
            secondOffset = null;
            return false;
        }

        if (firstPos.Partition == secondPos.Partition)
        {
            firstIndex++;
            secondIndex++;

            partition = firstPos.Partition;
            firstOffset = firstPos.Offset;
            secondOffset = secondPos.Offset;
        }
        else if (firstPos.Partition > secondPos.Partition)
        {
            secondIndex++;

            partition = secondPos.Partition;
            firstOffset = null;
            secondOffset = secondPos.Offset;
        }
        else
        {
            firstIndex++;

            partition = firstPos.Partition;
            firstOffset = firstPos.Offset;
            secondOffset = null;
        }

        return true;
    }

    private static bool TryGetElement(this StreamCoordinates coords, int index, out StreamPosition position)
    {
        if (index >= coords.Positions.Length)
        {
            position = default;
            return false;
        }

        position = coords.Positions[index];
        return true;
    }
}
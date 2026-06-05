using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Consumers.Helpers;

namespace Vostok.Hercules.Consumers.Tests.Helpers;

[TestFixture]
public class CoordinatesUtils_Tests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(5)]
    [TestCase(50)]
    public void Zip_WhenEqual(int count)
    {
        var first = CreateCoordinates(count);
        var second = CreateCoordinates(count);
        var expected = CreateExpected(count, [], []);

        CoordinatesUtils.Zip(first, second)
            .Should()
            .BeEquivalentTo(expected, o => o.WithStrictOrdering());
    }

    [TestCase(5, new[] {0})]
    [TestCase(5, new[] {2})]
    [TestCase(5, new[] {4})]
    [TestCase(5, new[] {0, 1})]
    [TestCase(5, new[] {1, 3})]
    [TestCase(5, new[] {2, 4})]
    [TestCase(5, new[] {0, 1, 3, 4})]
    [TestCase(5, new[] {1, 2, 3, 4})]
    public void Zip_FirstMissing(int count, int[] missingPartitions)
    {
        var first = CreateCoordinates(count, missingPartitions);
        var second = CreateCoordinates(count);
        var expected = CreateExpected(count, missingPartitions, []);

        CoordinatesUtils.Zip(first, second)
            .Should()
            .BeEquivalentTo(expected, o => o.WithStrictOrdering());
    }

    [TestCase(5, new[] {0})]
    [TestCase(5, new[] {2})]
    [TestCase(5, new[] {4})]
    [TestCase(5, new[] {0, 1})]
    [TestCase(5, new[] {1, 3})]
    [TestCase(5, new[] {2, 4})]
    [TestCase(5, new[] {0, 1, 3, 4})]
    [TestCase(5, new[] {1, 2, 3, 4})]
    public void Zip_SecondMissing(int count, int[] missingPartitions)
    {
        var first = CreateCoordinates(count);
        var second = CreateCoordinates(count, missingPartitions);
        var expected = CreateExpected(count, [], missingPartitions);

        CoordinatesUtils.Zip(first, second)
            .Should()
            .BeEquivalentTo(expected, o => o.WithStrictOrdering());
    }

    [TestCase(5, new[] {0}, new[] {1})]
    [TestCase(5, new[] {2}, new[] {0})]
    [TestCase(5, new[] {0, 1}, new[] {2, 3})]
    [TestCase(5, new[] {0}, new[] {1, 2, 3, 4})]
    public void Zip_Merge(int count, int[] missing1, int[] missing2)
    {
        var first = CreateCoordinates(count, missing1);
        var second = CreateCoordinates(count, missing2);
        var expected = CreateExpected(count, missing1, missing2);

        CoordinatesUtils.Zip(first, second)
            .Should()
            .BeEquivalentTo(expected, o => o.WithStrictOrdering());
    }

    private static StreamCoordinates CreateCoordinates(int count, int[] exceptPartitions = null) =>
        new StreamCoordinates(Enumerable
            .Range(0, count)
            .Except(exceptPartitions ?? [])
            .Select(x => new StreamPosition {Partition = x, Offset = x})
            .ToArray()
        );

    private static (int, long?, long?)[] CreateExpected(int count, int[] firstMissingPartitions, int[] secondMissingPartitions) =>
        Enumerable
            .Range(0, count)
            .Select(x => CreateResult(
                x,
                firstMissingPartitions.Contains(x) ? null : x,
                secondMissingPartitions.Contains(x) ? null : x
            ))
            .ToArray();

    private static (int, long?, long?) CreateResult(int partition, long? offset1, long? offset2) =>
        (partition, offset1, offset2);

    private static StreamPosition? NullPos => null;
}
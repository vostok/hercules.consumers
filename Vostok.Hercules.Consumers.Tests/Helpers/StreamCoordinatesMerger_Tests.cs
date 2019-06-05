using FluentAssertions;
using NUnit.Framework;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Consumers.Helpers;

namespace Vostok.Hercules.Consumers.Tests.Helpers
{
    [TestFixture]
    internal class StreamCoordinatesMerger_Tests
    {
        [Test]
        public void MergeMax_should_works_correctly()
        {
            var a = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1},
                    new StreamPosition {Partition = 2, Offset = 3},
                    new StreamPosition {Partition = 3, Offset = 4}
                });

            var b = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 2},
                    new StreamPosition {Partition = 2, Offset = 2},
                    new StreamPosition {Partition = 5, Offset = 4}
                });

            var max = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 2},
                    new StreamPosition {Partition = 2, Offset = 3},
                    new StreamPosition {Partition = 3, Offset = 4},
                    new StreamPosition {Partition = 5, Offset = 4}
                });
            StreamCoordinatesMerger.MergeMax(a, b).Positions.Should().BeEquivalentTo(max.Positions);
            StreamCoordinatesMerger.MergeMax(b, a).Positions.Should().BeEquivalentTo(max.Positions);
        }

        [Test]
        public void MergeMin_should_works_correctly()
        {
            var a = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1},
                    new StreamPosition {Partition = 2, Offset = 3},
                    new StreamPosition {Partition = 3, Offset = 4}
                });

            var b = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 2},
                    new StreamPosition {Partition = 2, Offset = 2},
                    new StreamPosition {Partition = 5, Offset = 4}
                });

            var min = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1},
                    new StreamPosition {Partition = 2, Offset = 2}
                });

            StreamCoordinatesMerger.MergeMin(a, b).Positions.Should().BeEquivalentTo(min.Positions);
            StreamCoordinatesMerger.MergeMin(b, a).Positions.Should().BeEquivalentTo(min.Positions);
        }

        [Test]
        public void FixInitialCoordinates_should_works_correctly()
        {
            var a = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1},
                    new StreamPosition {Partition = 2, Offset = 3},
                    new StreamPosition {Partition = 3, Offset = 4}
                });

            var b = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 2},
                    new StreamPosition {Partition = 2, Offset = 2},
                    new StreamPosition {Partition = 5, Offset = 4}
                });

            var @fixed = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1},
                    new StreamPosition {Partition = 2, Offset = 3},
                    new StreamPosition {Partition = 5, Offset = 0}
                });

            StreamCoordinatesMerger.FixInitialCoordinates(a, b).Positions.Should().BeEquivalentTo(@fixed.Positions);
        }

        [Test]
        public void Distance_should_works_correctly()
        {
            var a = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 100}
                });

            var b = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 1, Offset = 200},
                    new StreamPosition {Partition = 2, Offset = 2}
                });

            StreamCoordinatesMerger.Distance(a, b).Should().Be(100);
            StreamCoordinatesMerger.Distance(b, a).Should().Be(-100);
        }
    }
}
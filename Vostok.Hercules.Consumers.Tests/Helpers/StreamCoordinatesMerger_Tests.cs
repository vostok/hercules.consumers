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
        public void MergeMax_should_work_correctly()
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
            a.MergeMaxWith(b).Positions.Should().BeEquivalentTo(max.Positions);
            b.MergeMaxWith(a).Positions.Should().BeEquivalentTo(max.Positions);
        }

        [Test]
        public void MergeMin_should_work_correctly()
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
                    new StreamPosition {Partition = 2, Offset = 2},
                    new StreamPosition {Partition = 3, Offset = 4},
                    new StreamPosition {Partition = 5, Offset = 4}
                });

            a.MergeMinWith(b).Positions.Should().BeEquivalentTo(min.Positions);
            b.MergeMinWith(a).Positions.Should().BeEquivalentTo(min.Positions);
        }

        [Test]
        public void FilterBy_should_work_correctly()
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
                    new StreamPosition {Partition = 2, Offset = 3}
                });

            a.FilterBy(b).Positions.Should().BeEquivalentTo(@fixed.Positions);
        }

        [Test]
        public void Distance_should_work_correctly()
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

            a.DistanceTo(b).Should().Be(102);
            b.DistanceTo(a).Should().Be(-99);
        }
    }
}
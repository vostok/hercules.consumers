using FluentAssertions;
using NUnit.Framework;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Consumers.Helpers;

namespace Vostok.Hercules.Consumers.Tests.Helpers
{
    [TestFixture]
    internal class StreamCoordinatesExtensions_Tests
    {
        [Test]
        public void AdvancesOver_should_be_true_if_some_coordinates_increases()
        {
            var a = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 5},
                    new StreamPosition {Partition = 2, Offset = 5}
                });

            var b = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 2},
                    new StreamPosition {Partition = 2, Offset = 3}
                });

            a.AdvancesOver(b).Should().BeTrue();
            b.AdvancesOver(a).Should().BeFalse();
        }

        [Test]
        public void AdvancesOver_should_be_true_if_some_coordinates_added()
        {
            var a = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1},
                    new StreamPosition {Partition = 2, Offset = 1}
                });

            var b = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1}
                });

            a.AdvancesOver(b).Should().BeTrue();
            b.AdvancesOver(a).Should().BeFalse();
        }

        [Test]
        public void AdvancesOver_should_be_false_for_equal_coordinates()
        {
            var a = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1},
                    new StreamPosition {Partition = 2, Offset = 1}
                });

            a.AdvancesOver(a).Should().BeFalse();
        }
    }
}
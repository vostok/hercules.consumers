using FluentAssertions;
using NUnit.Framework;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Consumers.Helpers;

namespace Vostok.Hercules.Consumers.Tests.Helpers
{
    [TestFixture]
    internal class StreamCoordinatesSerializer_Tests
    {
        [Test]
        public void Serialize_Deserialize_should_works_correctly()
        {
            var a = new StreamCoordinates(
                new[]
                {
                    new StreamPosition {Partition = 0, Offset = 1},
                    new StreamPosition {Partition = 1, Offset = 1},
                    new StreamPosition {Partition = 2, Offset = 3},
                    new StreamPosition {Partition = 3, Offset = 4}
                });

            var serialized = StreamCoordinatesSerializer.Serialize(a);
            var b = StreamCoordinatesSerializer.Deserialize(serialized);

            b.Positions.Should().BeEquivalentTo(a.Positions);
        }

        [Test]
        public void Serialize_Deserialize_should_works_correctly_with_empty_coordinates()
        {
            var a = StreamCoordinates.Empty;

            var serialized = StreamCoordinatesSerializer.Serialize(a);
            var b = StreamCoordinatesSerializer.Deserialize(serialized);

            b.Positions.Should().BeEquivalentTo(a.Positions);
        }

        [Test]
        public void Deserialize_should_works_correctly_with_empty_data()
        {
            var coordinates = StreamCoordinatesSerializer.Deserialize(new byte[0]);
            coordinates.Positions.Should().BeEmpty();
        }

        [Test]
        public void Deserialize_should_works_correctly_with_null_data()
        {
            var coordinates = StreamCoordinatesSerializer.Deserialize(null);
            coordinates.Positions.Should().BeEmpty();
        }
    }
}
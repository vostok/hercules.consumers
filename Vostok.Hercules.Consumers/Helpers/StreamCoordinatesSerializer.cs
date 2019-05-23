using System.Collections.Generic;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class StreamCoordinatesSerializer
    {
        [NotNull]
        public static byte[] Serialize([NotNull] StreamCoordinates coordinates)
        {
            var builder = new StringBuilder();

            foreach (var position in coordinates.Positions)
            {
                builder
                    .Append(position.Partition)
                    .Append(" = ")
                    .Append(position.Offset)
                    .AppendLine();
            }

            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        [NotNull]
        public static StreamCoordinates Deserialize(byte[] serialized)
        {
            var reader = new StringReader(Encoding.UTF8.GetString(serialized));

            var positions = new List<StreamPosition>();

            while (true)
            {
                var line = reader.ReadLine();

                if (string.IsNullOrEmpty(line))
                    break;

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                positions.Add(
                    new StreamPosition
                    {
                        Partition = int.Parse(line.Substring(0, separatorIndex).Trim()),
                        Offset = int.Parse(line.Substring(separatorIndex + 1).Trim())
                    });
            }

            return new StreamCoordinates(positions.ToArray());
        }
    }
}
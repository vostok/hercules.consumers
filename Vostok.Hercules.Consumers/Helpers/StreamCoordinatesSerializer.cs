using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class StreamCoordinatesSerializer
    {
        private const string Separator = " = ";
        private static readonly Encoding Encoding = Encoding.UTF8;
        private static readonly byte[] SeparatorBytes = Encoding.GetBytes(Separator);

        [NotNull]
        public static byte[] Serialize([NotNull] StreamCoordinates coordinates)
        {
            using var stream = new MemoryStream();
            using var writer = new System.IO.StreamWriter(stream, Encoding);

            foreach (var position in coordinates.Positions)
            {
                writer.Write(position.Partition);
                writer.Write(Separator);
                writer.Write(position.Offset);
                writer.WriteLine();
            }

            writer.Flush();
            return stream.ToArray();
        }

#if NET8_0_OR_GREATER
        [NotNull]
        public static StreamCoordinates Deserialize([CanBeNull] byte[] serialized)
        {
            if (serialized is null || serialized.Length == 0)
                return StreamCoordinates.Empty;

            using var stream = new MemoryStream(serialized);
            using var reader = new System.IO.StreamReader(stream, Encoding);

            var positionsCount = serialized.Count(SeparatorBytes);
            var positions = new StreamPosition[positionsCount];
            var index = 0;

            while (reader.ReadLine() is {} line)
            {
                var lineSpan = line.AsSpan();
                var separatorIndex = lineSpan.IndexOf(Separator);
                if (separatorIndex < 0)
                    continue;

                positions[index++] = new StreamPosition
                {
                    Partition = int.Parse(lineSpan[..separatorIndex]),
                    Offset = long.Parse(lineSpan[(separatorIndex + Separator.Length)..]),
                };
            }

            return new StreamCoordinates(index < positionsCount ? positions.ToArray() : positions);
        }
#else
        [NotNull]
        public static StreamCoordinates Deserialize([CanBeNull] byte[] serialized)
        {
            if (serialized is null || serialized.Length == 0)
                return StreamCoordinates.Empty;

            using var stream = new MemoryStream(serialized);
            using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
            var positions = new List<StreamPosition>();

            while (true)
            {
                var line = reader.ReadLine();

                if (string.IsNullOrEmpty(line))
                    break;

                var separatorIndex = line.IndexOf(Separator, StringComparison.Ordinal);
                if (separatorIndex <= 0)
                    continue;

                positions.Add(
                    new StreamPosition
                    {
                        Partition = int.Parse(line.Substring(0, separatorIndex)),
                        Offset = long.Parse(line.Substring(separatorIndex + Separator.Length))
                    });
            }

            return new StreamCoordinates(positions.ToArray());
        }
#endif
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class StreamCoordinatesSerializer
    {
        private const string Separator = " = ";

        [NotNull]
        public static byte[] Serialize([NotNull] StreamCoordinates coordinates)
        {
            using var stream = new MemoryStream();
            using var writer = new System.IO.StreamWriter(stream, Encoding.UTF8);

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
    }
}
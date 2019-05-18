﻿using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class StreamCoordinatesExtensions
    {
        public static bool AdvancesOver([NotNull] this StreamCoordinates self, [NotNull] StreamCoordinates other)
        {
            var otherDictionary = other.ToDictionary();

            foreach (var position in self.Positions)
            {
                if (!otherDictionary.TryGetValue(position.Partition, out var otherPosition))
                    return true;

                if (position.Offset > otherPosition.Offset)
                    return true;
            }

            return false;
        }
    }
}
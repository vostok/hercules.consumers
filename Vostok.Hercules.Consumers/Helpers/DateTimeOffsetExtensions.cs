using System;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class DateTimeOffsetExtensions
    {
        public static bool InInterval(this DateTimeOffset timestamp, DateTimeOffset start, DateTimeOffset end)
        {
            return start <= timestamp && timestamp < end;
        }
    }
}
using System.Collections.Generic;
using Vostok.Logging.Abstractions;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class LogExtensions
    {
        public static ILog WithErrorsTransformation(this ILog log) =>
            log.WithLevelsTransformation(
                new Dictionary<LogLevel, LogLevel>
                {
                    {LogLevel.Error, LogLevel.Warn}
                });
    }
}
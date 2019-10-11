using System;
using JetBrains.Annotations;
using Vostok.Commons.Time;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public static class ConsumersConstants
    {
        public static readonly int EventsReadBatchSize = 10000;

        public static readonly int EventsWriteBatchSize = 10000;

        public static readonly TimeSpan EventsReadTimeout = 10.Seconds();

        public static readonly TimeSpan EventsWriteTimeout = 10.Seconds();

        public static readonly int EventsReadAttempts = 3;

        public static readonly TimeSpan DelayOnError = 2.Seconds();

        public static readonly TimeSpan DelayOnNoEvents = 2.Seconds();
    }
}
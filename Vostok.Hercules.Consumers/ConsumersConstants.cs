using System;
using JetBrains.Annotations;
using Vostok.Commons.Time;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public static class ConsumersConstants
    {
        public static readonly int EventsReadBatchSize = 50_000;

        public static readonly int EventsWriteBatchSize = 20_000;

        public static readonly int MaxPooledBufferSize = 128 * 1024 * 1024;

        public static readonly int MaxPooledBuffersPerBucket = 4;

        public static readonly TimeSpan EventsReadTimeout = 10.Seconds();

        public static readonly TimeSpan EventsWriteTimeout = 10.Seconds();

        public static readonly int EventsReadAttempts = 3;

        public static readonly TimeSpan DelayOnError = 1.Seconds();

        public static readonly TimeSpan DelayOnNoEvents = 1.Seconds();
    }
}
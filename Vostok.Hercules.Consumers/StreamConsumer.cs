using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Logging.Abstractions;

// ReSharper disable MethodSupportsCancellation

#pragma warning disable 4014

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamConsumer
    {
        private readonly StreamConsumer<HerculesEvent> consumer;

        public StreamConsumer([NotNull] StreamConsumerSettings settings, [CanBeNull] ILog log) =>
            consumer = new StreamConsumer<HerculesEvent>(settings, log);

        public Task RunAsync(CancellationToken cancellationToken) =>
            consumer.RunAsync(cancellationToken);
    }
}
using System;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Commons.Time;
using Vostok.Hercules.Client.Abstractions.Events;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class WindowedStreamConsumerSettings<T, TKey> : BatchesStreamConsumerSettings<T>
    {
        public WindowedStreamConsumerSettings(
            [NotNull] string streamName,
            [NotNull] Func<string> apiKeyProvider,
            [NotNull] IClusterProvider streamApiCluster,
            [NotNull] Func<T, TKey> keyProvider,
            [NotNull] Func<T, DateTimeOffset> timestampProvider,
            [NotNull] Func<TKey, IWindow> createWindow,
            [NotNull] Func<IBinaryBufferReader, IHerculesEventBuilder<T>> eventBuilderProvider,
            [NotNull] IStreamCoordinatesStorage leftCoordinatesStorage,
            [NotNull] IStreamCoordinatesStorage rightCoordinatesStorage,
            [NotNull] Func<StreamShardingSettings> shardingSettingsProvider)
            : base(
                streamName,
                apiKeyProvider,
                streamApiCluster,
                eventBuilderProvider,
                rightCoordinatesStorage,
                shardingSettingsProvider)
        {
            KeyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            TimestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
            CreateWindow = createWindow ?? throw new ArgumentNullException(nameof(createWindow));
            LeftCoordinatesStorage = leftCoordinatesStorage ?? throw new ArgumentNullException(nameof(leftCoordinatesStorage));
        }

        [NotNull]
        public Func<T, TKey> KeyProvider { get; }

        [NotNull]
        public Func<T, DateTimeOffset> TimestampProvider { get; }

        [NotNull]
        public Func<TKey, IWindow> CreateWindow { get; }

        [NotNull]
        public IStreamCoordinatesStorage LeftCoordinatesStorage { get; }

        public TimeSpan Period { get; set; } = 1.Minutes();
        public TimeSpan Lag { get; set; } = 30.Seconds();

        [CanBeNull]
        public Func<T, TimeSpan?> PeriodProvider { get; set; }
        [CanBeNull]
        public Func<T, TimeSpan?> LagProvider { get; set; }
        
        [CanBeNull]
        public Action<T> OnEventDrop { get; set; }

        public TimeSpan MaximumAllowedPeriod { get; set; } = 1.Minutes();
        public TimeSpan MaximumAllowedLag { get; set; } = 1.Minutes();

        public TimeSpan MaximumDeltaBeforeNow { get; set; } = 1.Days();
        public TimeSpan MaximumDeltaAfterNow { get; set; } = 5.Seconds();

        public interface IWindow
        {
            void Add([NotNull] T @event);

            void Flush(DateTimeOffset timestamp);
        }
    }
}
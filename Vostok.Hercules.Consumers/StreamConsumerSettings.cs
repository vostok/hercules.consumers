using System;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Consumers.Helpers;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamConsumerSettings : StreamConsumerSettings<HerculesEvent>
    {
        public StreamConsumerSettings(
            [NotNull] string streamName,
            [NotNull] IHerculesStreamClient streamClient,
            [NotNull] IStreamEventsHandler eventsHandler,
            [NotNull] IStreamCoordinatesStorage coordinatesStorage,
            [NotNull] Func<StreamShardingSettings> shardingSettingsProvider)
            : base(
                streamName,
                streamClient.ToGenericClient(),
                eventsHandler.ToGenericHandler(),
                coordinatesStorage,
                shardingSettingsProvider)
        {
        }
    }
}
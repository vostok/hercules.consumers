﻿using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Hercules.Client.Abstractions.Events;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Logging.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamReader
    {
        private readonly StreamReader<HerculesEvent> reader;

        public StreamReader([NotNull] StreamReaderSettings settings, [CanBeNull] ILog log)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            log = (log ?? LogProvider.Get()).ForContext<StreamReader>();

            reader = new StreamReader<HerculesEvent>(settings, log);
        }

        public Task<(ReadStreamQuery query, ReadStreamResult result)> ReadAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken = default) =>
            ReadAsync(coordinates, shardingSettings, int.MaxValue, cancellationToken);

        public async Task<(ReadStreamQuery query, ReadStreamResult result)> ReadAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            long additionalLimit,
            CancellationToken cancellationToken)
        {
            var (query, result) = await reader.ReadAsync(coordinates, shardingSettings, additionalLimit, cancellationToken).ConfigureAwait(false);
            return (query, result.FromGenericResult());
        }

        public Task<StreamCoordinates> SeekToEndAsync(
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken = default) =>
            reader.SeekToEndAsync(shardingSettings, cancellationToken);

        public Task<long?> CountStreamRemainingEventsAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken = default) =>
            reader.CountStreamRemainingEventsAsync(coordinates, shardingSettings, cancellationToken);
    }
}
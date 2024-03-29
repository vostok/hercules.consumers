﻿using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Helpers.Extensions;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Client.Abstractions.Results;
using Vostok.Hercules.Consumers.Helpers;
using Vostok.Logging.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class StreamReader<T>
    {
        private readonly StreamReaderSettings<T> settings;
        private readonly ILog log;

        public StreamReader([NotNull] StreamReaderSettings<T> settings, [CanBeNull] ILog log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = (log ?? LogProvider.Get()).ForContext<StreamReader<T>>();
        }

        public Task<(ReadStreamQuery query, ReadStreamResult<T> result)> ReadAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken = default) =>
            ReadAsync(coordinates, shardingSettings, int.MaxValue, cancellationToken);

        public async Task<(ReadStreamQuery query, ReadStreamResult<T> result)> ReadAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            long additionalLimit,
            CancellationToken cancellationToken = default)
        {
            log.Info(
                "Reading logical shard with index {ClientShard} from {ClientShardCount}.",
                shardingSettings.ClientShardIndex,
                shardingSettings.ClientShardCount);

            log.Debug("Current coordinates: {StreamCoordinates}.", coordinates);

            var eventsQuery = new ReadStreamQuery(settings.StreamName)
            {
                Coordinates = coordinates,
                ClientShard = shardingSettings.ClientShardIndex,
                ClientShardCount = shardingSettings.ClientShardCount,
                Limit = (int)Math.Min(settings.EventsReadBatchSize, additionalLimit)
            };

            ReadStreamResult<T> readResult;
            var remainingAttempts = settings.EventsReadAttempts;

            do
            {
                remainingAttempts--;
                readResult = await settings.StreamClient.ReadAsync(eventsQuery, settings.EventsReadTimeout, cancellationToken).ConfigureAwait(false);
                if (!readResult.IsSuccessful && !cancellationToken.IsCancellationRequested)
                {
                    log.Warn(
                        "Failed to read events from Hercules stream '{StreamName}'. " +
                        "Status: {Status}. Error: '{Error}'. " +
                        "Will try again {RemainingAttempts} more times.",
                        settings.StreamName,
                        readResult.Status,
                        readResult.ErrorDetails,
                        remainingAttempts);
                    if (remainingAttempts > 0)
                        await Task.Delay(settings.DelayOnError, cancellationToken).SilentlyContinue().ConfigureAwait(false);
                }
            } while (!cancellationToken.IsCancellationRequested && !readResult.IsSuccessful && remainingAttempts > 0);

            if (readResult.IsSuccessful)
            {
                log.Info(
                    "Read {EventsCount} event(s) from Hercules stream '{StreamName}'.",
                    readResult.Payload.Events.Count,
                    settings.StreamName);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                log.Info("Cancelled read events request from Hercules stream '{StreamName}'.",
                    settings.StreamName);
            }
            else
            {
                log.Error(
                    "Failed to read events from Hercules stream '{StreamName}'. " +
                    "Status: {Status}. Error: '{Error}'.",
                    settings.StreamName,
                    readResult.Status,
                    readResult.ErrorDetails);
            }

            return (eventsQuery, readResult);
        }

        public async Task<StreamCoordinates> SeekToEndAsync(
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken = default)
        {
            var seekToEndQuery = new SeekToEndStreamQuery(settings.StreamName)
            {
                ClientShard = shardingSettings.ClientShardIndex,
                ClientShardCount = shardingSettings.ClientShardCount
            };

            var end = await settings.StreamClient.SeekToEndAsync(seekToEndQuery, settings.EventsReadTimeout, cancellationToken).ConfigureAwait(false);
            return end.Payload.Next;
        }

        public async Task<long?> CountStreamRemainingEventsAsync(
            StreamCoordinates coordinates,
            StreamShardingSettings shardingSettings,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var endCoordinates = await SeekToEndAsync(shardingSettings, cancellationToken).ConfigureAwait(false);

                var distance = coordinates.DistanceTo(endCoordinates);

                log.Debug(
                    "Stream remaining events: {Count}. Current coordinates: {CurrentCoordinates}, end coordinates: {EndCoordinates}.",
                    distance,
                    coordinates,
                    endCoordinates);

                return distance;
            }
            catch (Exception e)
            {
                log.Warn(e, "Failed to count remaining events.");
                return null;
            }
        }
    }
}
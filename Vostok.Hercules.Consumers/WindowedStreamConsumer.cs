using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Commons.Time;
using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Hercules.Client.Abstractions.Queries;
using Vostok.Hercules.Consumers.Helpers;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Context;
using Vostok.Metrics;
using Vostok.Metrics.Grouping;
using Vostok.Metrics.Primitives.Gauge;
using Vostok.Metrics.Primitives.Timer;
using Vostok.Tracing.Abstractions;

// ReSharper disable ParameterHidesMember

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class WindowedStreamConsumer<T, TKey> : BatchesStreamConsumer<T>
    {
        private readonly WindowedStreamConsumerSettings<T, TKey> settings;
        private readonly ILog log;
        private readonly ITracer tracer;
        private readonly IMetricGroup1<IIntegerGauge> stateMetric;
        private readonly Dictionary<TKey, Windows<T, TKey>> windows;

        private volatile StreamCoordinates leftCoordinates;

        public WindowedStreamConsumer([NotNull] WindowedStreamConsumerSettings<T, TKey> settings, [CanBeNull] ILog log)
            : base(settings, log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = (log ?? LogProvider.Get()).ForContext<WindowedStreamConsumer<T, TKey>>();
            tracer = settings.Tracer ?? TracerProvider.Get();

            windows = new Dictionary<TKey, Windows<T, TKey>>();

            var settingsOnBatchEnd = settings.OnBatchEnd;
            settings.OnBatchEnd = c =>
            {
                FlushWindows();
                settings.LeftCoordinatesStorage.AdvanceAsync(leftCoordinates);
                settingsOnBatchEnd?.Invoke(c);
            };

            var settingsOnEvent = settings.OnEvent;
            settings.OnEvent = (e, c) =>
            {
                AddEvent(e, c);
                settingsOnEvent?.Invoke(e, c);
            };

            var settingsOnRestart = settings.OnRestart;
            settings.OnRestart = c =>
            {
                Restart(c).GetAwaiter().GetResult();
                settingsOnRestart?.Invoke(c);
            };

            var metricContext = settings.MetricContext ?? new DevNullMetricContext();
            stateMetric = metricContext.CreateIntegerGauge("state", "type");
        }

        private async Task Restart(StreamCoordinates rightCoordinates)
        {
            await RestartCoordinates(rightCoordinates).ConfigureAwait(false);

            try
            {
                RestartWindows(rightCoordinates).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                log.Error(e, "Failed to restart windows.");
                windows.Clear();
            }
        }

        private async Task RestartCoordinates(StreamCoordinates rightCoordinates)
        {
            var storageCoordinates = await settings.LeftCoordinatesStorage.GetCurrentAsync().ConfigureAwait(false);
            storageCoordinates = storageCoordinates.FilterBy(rightCoordinates);
            LogCoordinates("Storage left", storageCoordinates);

            if (storageCoordinates.Positions.Length < rightCoordinates.Positions.Length)
            {
                log.Info("Some coordinates are missing. Returning right coordinates.");
                leftCoordinates = rightCoordinates;
                return;
            }

            log.Info("Returning storage coordinates.");
            leftCoordinates = storageCoordinates;
        }

        private async Task RestartWindows(StreamCoordinates rightCoordinates)
        {
            LogCoordinates("Current", leftCoordinates, rightCoordinates);

            windows.Clear();

            var partitionsCount = await GetPartitionsCount().ConfigureAwait(false);

            foreach (var position in leftCoordinates.Positions)
            {
                var start = position.Offset;
                var end = rightCoordinates.Positions.Single(p => p.Partition == position.Partition).Offset;

                while (start < end)
                {
                    start = await RestartPartition(position.Partition, partitionsCount, start, end).ConfigureAwait(false);
                }
            }
        }

        private async Task<long> RestartPartition(int partition, int partitionsCount, long start, long end)
        {
            var query = new ReadStreamQuery(settings.StreamName)
            {
                Coordinates = new StreamCoordinates(new[] {new StreamPosition {Offset = start, Partition = partition}}),
                ClientShard = partition,
                ClientShardCount = partitionsCount,
                Limit = (int)Math.Min(end - start, settings.EventsReadBatchSize)
            };

            var result = await ReadAsync(query).ConfigureAwait(false);

            try
            {
                HandleEvents(result, query.Coordinates);

                foreach (var window in windows)
                    window.Value.Flush(true);
            }
            finally
            {
                result.Dispose();
            }

            return result.Next.Positions.Single().Offset;
        }

        private void AddEvent(T @event, StreamCoordinates queryCoordinates)
        {
            var key = settings.KeyProvider(@event);
            if (!windows.ContainsKey(key))
                windows[key] = new Windows<T, TKey>(key, settings);
            if (!windows[key].AddEvent(@event, queryCoordinates) && !restart)
                eventsMetric?.For("dropped").Increment();
        }

        private void FlushWindows()
        {
            using (new OperationContextToken("FlushEvents"))
            using (var operationSpan = tracer.BeginConsumerCustomOperationSpan("FlushEvents"))    
            using (iterationMetric?.For("flush_time").Measure())
            {
                var result = new WindowsFlushResult();
                var stale = new List<TKey>();

                foreach (var pair in windows)
                {
                    var flushResult = pair.Value.Flush();
                    result.MergeWith(flushResult);

                    if (flushResult.EventsCount == 0
                        && DateTimeOffset.UtcNow - pair.Value.LastEventAdded > 1.Hours())
                        stale.Add(pair.Key);
                }

                foreach (var s in stale)
                    windows.Remove(s);

                if (result.EventsCount > 0)
                    leftCoordinates = result.FirstEventCoordinates;

                log.Info(
                    "Consumer status: keys: {KeysCount}, windows: {WindowsCount}, events: {EventsCount}.",
                    windows.Count,
                    result.WindowsCount,
                    result.EventsCount);
                
                operationSpan.SetOperationDetails(result.EventsCount);
                operationSpan.SetSuccess();
                
                stateMetric?.For("keys").Set(windows.Count);
                stateMetric?.For("windows").Set(result.WindowsCount);
                stateMetric?.For("events").Set(result.EventsCount);
            }
        }

        private async Task<int> GetPartitionsCount()
        {
            var end = await SeekToEndAsync(new StreamShardingSettings(0, 1)).ConfigureAwait(false);
            return end.Positions.Length;
        }

        private void LogCoordinates(string message, StreamCoordinates left, StreamCoordinates right)
        {
            LogCoordinates(message + " left", left);
            LogCoordinates(message + " right", right);
        }
    }
}
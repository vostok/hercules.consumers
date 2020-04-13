using System;
using System.Collections.Generic;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal class Windows<T, TKey>
    {
        public DateTimeOffset LastEventAdded;
        private readonly TKey key;
        private readonly WindowedStreamConsumerSettings<T, TKey> settings;

        private readonly List<Window<T, TKey>> windows = new List<Window<T, TKey>>();
        private DateTimeOffset minimumAllowedTimestamp = DateTimeOffset.MinValue;
        private DateTimeOffset maximumObservedTimestamp = DateTimeOffset.MinValue;

        public Windows(TKey key, WindowedStreamConsumerSettings<T, TKey> settings)
        {
            this.key = key;
            this.settings = settings;
            LastEventAdded = DateTimeOffset.UtcNow;
        }

        public bool AddEvent(T @event, StreamCoordinates coordinates)
        {
            var now = DateTimeOffset.Now;
            var timestamp = settings.TimestampProvider(@event);

            if (!timestamp.InInterval(now - settings.MaximumDeltaBeforeNow, now + settings.MaximumDeltaAfterNow))
                return false;
            if (timestamp < minimumAllowedTimestamp)
                return false;

            if (maximumObservedTimestamp < timestamp)
                maximumObservedTimestamp = timestamp;

            foreach (var window in windows)
            {
                if (window.AddEvent(@event, timestamp))
                    return true;
            }

            var newWindow = CreateWindow(timestamp, coordinates);
            newWindow.AddEvent(@event, timestamp);
            windows.Add(newWindow);
            LastEventAdded = now.UtcDateTime;
            return true;
        }

        public WindowsFlushResult Flush(bool restartPhase = false)
        {
            var result = new WindowsFlushResult();

            for (var i = 0; i < windows.Count; i++)
            {
                var window = windows[i];

                if (window.ShouldBeClosedBefore(maximumObservedTimestamp) || window.ExistsForTooLong(restartPhase))
                {
                    windows.RemoveAt(i--);

                    window.Flush();

                    if (minimumAllowedTimestamp < window.End)
                        minimumAllowedTimestamp = window.End;
                }
                else
                {
                    result.AddWindow(window.EventsCount, window.FirstEventCoordinates);
                }
            }

            return result;
        }
        
        private Window<T, TKey> CreateWindow(DateTimeOffset timestamp, StreamCoordinates coordinates)
        {
            var period = settings.Period;
            var lag = settings.Lag;

            var start = timestamp.AddTicks(-timestamp.Ticks % period.Ticks);
            var result = new Window<T, TKey>(settings.CreateWindow(key), coordinates, start, start + period, period, lag);
            return result;
        }
    }
}
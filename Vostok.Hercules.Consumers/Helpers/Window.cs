using System;
using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal class Window<T, TKey>
    {
        public readonly StreamCoordinates FirstEventCoordinates;
        public readonly DateTimeOffset Start;
        public readonly DateTimeOffset End;
        private readonly WindowedStreamConsumerSettings<T, TKey>.IWindow implementation;
        private readonly TimeSpan period;
        private readonly TimeSpan lag;

        private DateTimeOffset lastEventAdded;

        internal Window(WindowedStreamConsumerSettings<T, TKey>.IWindow implementation, StreamCoordinates firstEventCoordinates, DateTimeOffset start, DateTimeOffset end, TimeSpan period, TimeSpan lag)
        {
            this.implementation = implementation;
            FirstEventCoordinates = firstEventCoordinates;
            Start = start;
            End = end;
            this.period = period;
            this.lag = lag;
            lastEventAdded = DateTimeOffset.Now;
        }

        public int EventsCount { get; private set; }

        public bool AddEvent(T @event, DateTimeOffset timestamp)
        {
            if (!timestamp.InInterval(Start, End))
                return false;

            lastEventAdded = DateTimeOffset.Now;
            EventsCount++;
            implementation.Add(@event);

            return true;
        }

        public bool ShouldBeClosedBefore(DateTimeOffset timestamp) =>
            End + lag <= timestamp;

        public bool ExistsForTooLong(bool restartPhase = false)
        {
            if (restartPhase)
            {
                lastEventAdded = DateTimeOffset.Now;
                return false;
            }

            return DateTimeOffset.Now - lastEventAdded > period + lag;
        }

        public void Flush() =>
            implementation.Flush(End);

        public override string ToString() =>
            $"{nameof(FirstEventCoordinates)}: {FirstEventCoordinates}, {nameof(Start)}: {Start}, {nameof(End)}: {End}, {nameof(period)}: {period}, {nameof(lag)}: {lag}, {nameof(lastEventAdded)}: {lastEventAdded}, {nameof(EventsCount)}: {EventsCount}";
    }
}
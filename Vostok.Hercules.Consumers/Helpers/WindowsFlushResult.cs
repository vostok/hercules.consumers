using Vostok.Hercules.Client.Abstractions.Models;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal class WindowsFlushResult
    {
        public StreamCoordinates FirstEventCoordinates = StreamCoordinates.Empty;
        public long EventsCount;
        public long WindowsCount;

        public void AddWindow(int eventsCount, StreamCoordinates firstEventCoordinates)
        {
            WindowsCount++;
            EventsCount += eventsCount;
            AddFirstActiveEventCoordinates(firstEventCoordinates);
        }

        public void MergeWith(WindowsFlushResult other)
        {
            AddFirstActiveEventCoordinates(other.FirstEventCoordinates);
            EventsCount += other.EventsCount;
            WindowsCount += other.WindowsCount;
        }

        private void AddFirstActiveEventCoordinates(StreamCoordinates coordinates) =>
            FirstEventCoordinates = StreamCoordinatesMerger.MergeMin(FirstEventCoordinates, coordinates);
    }
}
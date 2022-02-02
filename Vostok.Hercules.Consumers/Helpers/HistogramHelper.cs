using Vostok.Metrics.Primitives.Timer;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class HistogramHelper
    {
        public static HistogramBuckets CreateDefaultBuckets()
        {
            var upperBounds = new double[30];

            upperBounds[0] = 0;
            upperBounds[1] = 1;
            for (var i = 2; i < upperBounds.Length; i++)
                upperBounds[i] = upperBounds[i - 1] * 2;

            return new HistogramBuckets(upperBounds);
        }
    }
}
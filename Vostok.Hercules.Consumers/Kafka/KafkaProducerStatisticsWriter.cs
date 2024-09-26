#if NET6_0_OR_GREATER // because netcoreapp3.1 doesn't contain STJ to deserialize librdkafka statistics json

using Confluent.Kafka;
using Vostok.Metrics;
using Vostok.Metrics.Primitives.Gauge;
using System.Text.Json.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Vostok.Hercules.Consumers.Kafka;

internal class KafkaProducerStatisticsHandler
{
    private readonly IIntegerGauge txQueuedCount;
    private readonly IIntegerGauge txQueuedSize;
    private readonly IIntegerGauge sentMessagesCount;

    public KafkaProducerStatisticsHandler(IMetricContext metricContext)
    {
        txQueuedCount = metricContext.CreateIntegerGauge("producer_queue_count");
        txQueuedSize = metricContext.CreateIntegerGauge("producer_queue_size");
        sentMessagesCount = metricContext.CreateIntegerGauge("sent_messages");
    }

    public void Write<TKey, TValue>(IProducer<TKey, TValue> producer, string statisticsJson)
    {
        SendStats(JsonSerializer.Deserialize<Statistics>(statisticsJson));
    }

    private void SendStats(Statistics statistics)
    {
        txQueuedCount.TryIncreaseTo(statistics.ProducerQueueCount);
        txQueuedSize.TryIncreaseTo(statistics.ProducerQueueBytesSize);
        sentMessagesCount.TryIncreaseTo(statistics.SentMessages);
    }

    /// <remarks>
    ///     See <a href="https://github.com/confluentinc/librdkafka/blob/master/STATISTICS.md">librdkafka statistics format</a>
    /// </remarks>
    private class Statistics
    {
        [JsonPropertyName("msg_cnt")]
        public long ProducerQueueCount { get; set; }

        [JsonPropertyName("msg_size")]
        public long ProducerQueueBytesSize { get; set; }

        [JsonPropertyName("txmsgs")]
        public long SentMessages { get; set; }
    }
}
#endif
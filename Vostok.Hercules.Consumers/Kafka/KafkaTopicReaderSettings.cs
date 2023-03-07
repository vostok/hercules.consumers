using JetBrains.Annotations;

namespace Vostok.Hercules.Consumers.Kafka;

internal sealed class KafkaTopicReaderSettings
{
    [NotNull]
    public string BootstrapServers { get; }
    
    [NotNull]
    public string GroupId { get; }
    
    [NotNull]
    public string Topic { get; }

    public int FetchMinBytes { get; set; } = 1;

    public int FetchWaitMaxMs { get; set; } = 500;

    public KafkaTopicReaderSettings(string bootstrapServers, string groupId, string topic)
    {
        BootstrapServers = bootstrapServers;
        GroupId = groupId;
        Topic = topic;
    }
}
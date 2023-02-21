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

    public KafkaTopicReaderSettings(string bootstrapServers, string groupId, string topic)
    {
        BootstrapServers = bootstrapServers;
        GroupId = groupId;
        Topic = topic;
    }
}
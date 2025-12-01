using Confluent.Kafka;
using Confluent.Kafka.Admin; 

namespace KafkaProducerApi.Helpers
{
public class TopicInitializer
{
    public static async Task InitTopics(string bootstrapServers)
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();

        var topics = new[] { "http-200", "http-300", "http-400", "http-404", "http-500" };

        foreach (var topicName in topics)
        {
            try
            {
                // Partition ve Replication Factor ayarları
                await adminClient.CreateTopicsAsync(new[]
                {
                    new TopicSpecification { Name = topicName, NumPartitions = 3, ReplicationFactor = 3 }
                });
                Console.WriteLine($"Topic Oluşturuldu: {topicName}");
            }
            // Topic zaten varsa sistem kendini kapatmasın diye.
            catch (CreateTopicsException e) when (e.Results[0].Error.Code == ErrorCode.TopicAlreadyExists) 
            {
                Console.WriteLine($"ℹTopic mevcut: {topicName}");
            }
        }
    }
}
}
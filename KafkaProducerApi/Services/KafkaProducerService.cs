using Confluent.Kafka;
using System.Text.Json;

namespace KafkaProducerApi.Services;

public class KafkaProducerService : IKafkaProducerService, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(ILogger<KafkaProducerService> logger)
    {
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = "localhost:9092",  
            ClientId = "aspnet-producer",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 1000,
            CompressionType = CompressionType.Snappy,
            LingerMs = 10,
            BatchSize = 16384
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetKeySerializer(Serializers.Utf8)
            .SetValueSerializer(Serializers.Utf8)
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka Error: {Reason}", error.Reason);
            })
            .Build();

        _logger.LogInformation("Kafka Producer başlatıldı: {BootstrapServers}", config.BootstrapServers);
    }

    public async Task<DeliveryResult<string, string>> ProduceAsync<T>(string topic, string key, T message)
    {
        try
        {
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var kafkaMessage = new Message<string, string>
            {
                Key = key,
                Value = json,
                Timestamp = Timestamp.Default
            };

            var result = await _producer.ProduceAsync(topic, kafkaMessage);

            _logger.LogInformation(
                "✅ Mesaj gönderildi - Topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
                result.Topic, result.Partition.Value, result.Offset.Value);

            return result;
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "❌ Mesaj gönderilemedi - Topic: {Topic}, Error: {Error}",
                topic, ex.Error.Reason);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(10));
        _producer?.Dispose();
        _logger.LogInformation("Kafka Producer kapatıldı");
    }
}
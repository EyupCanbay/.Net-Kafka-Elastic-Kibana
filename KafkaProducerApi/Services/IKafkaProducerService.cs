using Confluent.Kafka;

namespace KafkaProducerApi.Services;

public interface IKafkaProducerService
{
    Task<DeliveryResult<string, string>> ProduceAsync<T>(string topic, string key, T message);
}
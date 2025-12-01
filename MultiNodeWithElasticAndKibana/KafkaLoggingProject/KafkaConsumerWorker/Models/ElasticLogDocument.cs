



using System;
using System.Text.Json.Serialization;
namespace KafkaConsumerWorker.Models
{   
public class ElasticLogDocument
{
    [JsonPropertyName("traceId")]
    public string TraceId { get; set; }

    [JsonPropertyName("httpMethod")]
    public string HttpMethod { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("kafkaTopic")]
    public string KafkaTopic { get; set; }

    [JsonPropertyName("kafkaOffset")]
    public long KafkaOffset { get; set; }
}
}
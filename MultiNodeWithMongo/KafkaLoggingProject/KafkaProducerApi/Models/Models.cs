namespace KafkaProducerApi.Models
{
    // Bu model hem Producer hem Consumer için ortak olmalıdır.
    public class HttpLogModel
    {
        // Varsayılan değer ataması CS8618 uyarısını giderir.
        public string TraceId { get; set; } 
        public string HttpMethod { get; set; } 
        public string Path { get; set; } 
        public int StatusCode { get; set; }
        public long DurationMs { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
using Confluent.Kafka;
using Elastic.Clients.Elasticsearch;
using System.Text.Json;
using System.Text.Json.Serialization; // Bunu eklemeyi unutma
using KafkaProducerApi.Models;

// Elasticsearch dökümanı için model
// Elastic'te alan adlarının "traceId" gibi görünmesi için JsonPropertyName ekledik.
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

class Program
{
    static async Task Main(string[] args)
    {
        // JSON Ayarları (HAYAT KURTARAN KISIM BURASI)
        // Kafka'dan gelen "traceId"yi C#'taki "TraceId" ile eşleştirmesini sağlar.
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // 1. Elasticsearch Bağlantı Ayarları
        var settings = new ElasticsearchClientSettings(new Uri("http://localhost:9200"))
            .DefaultIndex("http-logs-index");

        var elasticClient = new ElasticsearchClient(settings);

        // Ping ile kontrol
        var ping = await elasticClient.PingAsync();
        if (!ping.IsValidResponse)
        {
            Console.WriteLine($"❌ Elastic Bağlantı Hatası: {ping.DebugInformation}");
            return; 
        }
        Console.WriteLine("✅ Elastic bağlantısı OK.");

        // 2. Kafka Ayarları
        var config = new ConsumerConfig
        {
            BootstrapServers = "localhost:9092,localhost:9093,localhost:9094",
            GroupId = "log-processor-group-elastic",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(new[] { "http-200", "http-300", "http-400", "http-404", "http-500" });

        Console.WriteLine("🎧 Consumer dinlemeye başladı...");

        try
        {
            while (true)
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(1));

                if (result == null) continue;

                try 
                {
                    // KRİTİK DÜZELTME: jsonOptions parametresini buraya ekledik!
                    var logData = JsonSerializer.Deserialize<HttpLogModel>(result.Message.Value, jsonOptions);

                    if (logData != null)
                    {
                        var elasticDoc = new ElasticLogDocument
                        {
                            TraceId = logData.TraceId,
                            HttpMethod = logData.HttpMethod,
                            Path = logData.Path,
                            StatusCode = logData.StatusCode,
                            DurationMs = logData.DurationMs,
                            Timestamp = logData.Timestamp, // Artık 0001 gelmeyecek
                            KafkaTopic = result.Topic,
                            KafkaOffset = result.Offset.Value
                        };

                        // ID oluştururken null kontrolü yapalım ki patlamasın
                        var documentId = $"{elasticDoc.TraceId ?? Guid.NewGuid().ToString()}-{result.Offset.Value}";
                        
                        var response = await elasticClient.IndexAsync(elasticDoc, documentId);

                        if (response.IsValidResponse)
                        {
                            consumer.Commit(result);
                            Console.WriteLine($"✅ Kaydedildi [Topic: {result.Topic}] Code: {elasticDoc.StatusCode}");
                        }
                        else
                        {
                            Console.WriteLine($"❌ Elastic Yazma Hatası: {response.DebugInformation}");
                        }
                    }
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"⚠️ JSON Format Hatası (Atlanıyor): {jsonEx.Message}");
                    // Hatalı JSON varsa consumer durmasın, commit edip geçsin veya Dead Letter Queue'ya atsın.
                    // Şimdilik offset'i ilerletmiyoruz, manuel incelersin.
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Kritik Hata: {ex.Message}");
        }
        finally
        {
            consumer.Close();
        }
    }
}
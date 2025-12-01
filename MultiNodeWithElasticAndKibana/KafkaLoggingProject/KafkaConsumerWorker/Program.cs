using Confluent.Kafka;
using Elastic.Clients.Elasticsearch;
using System.Text.Json;
using System.Text.Json.Serialization; 
using KafkaProducerApi.Models;
using KafkaConsumerWorker.Models;



class Program
{
    static async Task Main(string[] args)
    {
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
            Console.WriteLine($"Elastic Bağlantı Hatası: {ping.DebugInformation}");
            return; 
        }
        Console.WriteLine(" Elastic bağlantısı OK.");

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
                    var logData = JsonSerializer.Deserialize<HttpLogModel>(result.Message.Value, jsonOptions);

                    if (logData != null)
                    {
                        var elasticDoc = new KafkaConsumerWorker.Models.ElasticLogDocument
                        {
                            TraceId = logData.TraceId,
                            HttpMethod = logData.HttpMethod,
                            Path = logData.Path,
                            StatusCode = logData.StatusCode,
                            DurationMs = logData.DurationMs,
                            Timestamp = logData.Timestamp, 
                            KafkaTopic = result.Topic,
                            KafkaOffset = result.Offset.Value
                        };

                        var documentId = $"{elasticDoc.TraceId ?? Guid.NewGuid().ToString()}-{result.Offset.Value}";
                        
                        var response = await elasticClient.IndexAsync(elasticDoc, documentId);

                        if (response.IsValidResponse)
                        {
                            consumer.Commit(result);
                            Console.WriteLine($" Kaydedildi [Topic: {result.Topic}] Code: {elasticDoc.StatusCode}");
                        }
                        else
                        {
                            Console.WriteLine($" Elastic Yazma Hatası: {response.DebugInformation}");
                        }
                    }
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($" JSON Format Hatası (Atlanıyor): {jsonEx.Message}");
                    // Hatalı JSON varsa consumer durmasın, commit edip geçsin veya Dead Letter Queue'ya atsın.
                    // Şimdilik offset'i ilerletmiyoruz, manuel incelersin.
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Kritik Hata: {ex.Message}");
        }
        finally
        {
            consumer.Close();
        }
    }
}
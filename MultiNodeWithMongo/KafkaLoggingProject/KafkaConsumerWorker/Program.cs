using Confluent.Kafka;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;
using KafkaProducerApi.Models; // HttpLogModel'in bulunduğu namespace'e referans (veya modeli buraya taşı)

class Program
{
    static async Task Main(string[] args)
    {
        // 1. Mongo Bağlantısı
        // NOT: API ile aynı makinede çalışıyorsan, localhost üzerinden docker'a bağlan.
        // YENİ HALİ: authSource=admin parametresi eklendi
        var mongoClient = new MongoClient("mongodb://admin:password@localhost:27017/logs?authSource=admin");
        var database = mongoClient.GetDatabase("logs");
        var collection = database.GetCollection<BsonDocument>("http_logs");

        // 2. Kafka Ayarları
        var config = new ConsumerConfig
        {
            // Docker dışından bağlanıyorsan external portları kullan
            BootstrapServers = "localhost:9092,localhost:9093,localhost:9094",
            GroupId = "log-processor-group", 
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false // Manuel Commit
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();

        // 3. Tüm Topiclere Abone Ol
        consumer.Subscribe(new[] { "http-200", "http-300", "http-400", "http-404", "http-500" });

        Console.WriteLine("🎧 Consumer dinlemeye başladı...");

        try
        {
            while (true)
            {
                try
                {
                    // Timeout ile oku
                    var result = consumer.Consume(TimeSpan.FromSeconds(1));

                    if (result != null)
                    {
                        Console.WriteLine($"📥 Mesaj alındı | Topic: {result.Topic} | Status: {result.Message.Key}");

                        // Deserialize et
                        // CS8602 uyarısını gidermek için null kontrolü eklendi.
                        var logData = JsonSerializer.Deserialize<HttpLogModel>(result.Message.Value);

                        if (logData != null) 
                        {
                            // Mongo Document Hazırla
                            var document = new BsonDocument
                            {
                                { "traceId", logData.TraceId },
                                { "topic", result.Topic }, 
                                { "statusCode", logData.StatusCode },
                                { "path", logData.Path },
                                { "durationMs", logData.DurationMs },
                                { "timestamp", logData.Timestamp },
                                { "kafkaOffset", result.Offset.Value }
                            };

                            // Mongo'ya Yaz
                            await collection.InsertOneAsync(document);

                            // İşlem başarılıysa Commit at
                            consumer.Commit(result);
                            Console.WriteLine($"   -> MongoDB'ye kaydedildi.");
                        }
                    }
                }
                catch (ConsumeException ex) when (ex.Error.IsFatal)
                {
                    Console.WriteLine($"❌ ÖLÜMCÜL HATA: {ex.Error.Reason}");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ İşleme Hatası: {ex.Message}");
                    // Hata olsa bile commit atılmadığı için mesaj tekrar denenecektir.
                    await Task.Delay(1000); 
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Kapanıyor...");
        }
        finally
        {
            consumer.Close();
        }
    }
}
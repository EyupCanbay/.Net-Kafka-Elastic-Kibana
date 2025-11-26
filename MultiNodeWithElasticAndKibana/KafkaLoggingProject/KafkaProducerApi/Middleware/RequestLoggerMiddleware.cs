using System.Diagnostics; 
using System.Text.Json;
using KafkaProducerApi.Services;
using KafkaProducerApi.Models;
using System.Collections.Generic; // List ve Random için eklendi

namespace KafkaProducerApi.Middleware
{
    public class RequestLoggerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Random _random = new Random(); // Rastgelelik için sınıf seviyesinde tanımlandı.

        // Başarılı isteklerin rastgele gönderileceği topic'ler
        private readonly List<string> _successTopics = new List<string> 
        {
            "http-200", 
            "http-300",
            "http-400" // Buraya istersen sadece http-200'ü veya yeni topicler ekleyebilirsin
                       // Ancak mimariyi bozmamak adına tüm topicleri listeledim
        };
        
        public RequestLoggerMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, IKafkaProducerService producer)
        {
            var stopwatch = Stopwatch.StartNew();
            
            await _next(context);
            
            stopwatch.Stop();

            int statusCode = context.Response.StatusCode;
            string topicName;

            // Başarılı isteği yakala ve rastgele topic ata
            if (statusCode >= 200 && statusCode < 300)
            {
                topicName = GetRandomSuccessTopic();
            }
            else
            {
                // Hatalı istekleri eskisi gibi Status Code'a göre yönlendir
                topicName = GetErrorTopicName(statusCode);
            }

            var logModel = new HttpLogModel
            {
                TraceId = context.TraceIdentifier , 
                HttpMethod = context.Request.Method ,
                Path = context.Request.Path.ToString() ,
                StatusCode = statusCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Timestamp = DateTime.UtcNow
            };

            // Asenkron olarak Kafka'ya gönder
            _ = producer.ProduceAsync(topicName, logModel.TraceId, logModel);
        }

        // Rastgele başarılı topic seçen yeni metot
        private string GetRandomSuccessTopic()
        {
            // successTopics listesinden rastgele bir index seç
            int index = _random.Next(_successTopics.Count);
            return _successTopics[index];
        }

        // Hata durumlarını eskisi gibi yönlendiren metot (Ismi GetTopicName'den GetErrorTopicName'e değişti)
        private string GetErrorTopicName(int statusCode)
        {
             return statusCode switch
            {
                // Başarılı durumlar artık yukarıda (Invoke içinde) rastgele yönlendiriliyor.
                // Burada sadece hata kodları ele alınır.
                >= 200 and < 300 => _successTopics[0], // Normalde bu satıra girmez, güvenlik için varsayılan topic atandı.
                >= 300 and < 400 => "http-300",
                404 => "http-404",
                >= 400 and < 500 => "http-400",
                >= 500 => "http-500",
                _ => "http-unknown"
            };
        }
    }
}
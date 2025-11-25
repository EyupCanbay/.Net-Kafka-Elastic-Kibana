


using KafkaProducerApi.Middleware;

using KafkaProducerApi.Services;
using KafkaProducerApi.Helpers; // TopicInitializer için eklendi
using Microsoft.AspNetCore.Mvc; // Örnek endpoint'ler için eklendi

var builder = WebApplication.CreateBuilder(args); // <-- CS0103 builder hatası düzeltildi.

// Servis Tanımları
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();

var app = builder.Build();

// Kafka Topiclerini Başlat
// NOT: BootstrapServers, docker-compose'daki external portları kullanmalı.
await TopicInitializer.InitTopics("localhost:9092,localhost:9093,localhost:9094");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Global Loglama Middleware'ini Entegre Et
app.UseMiddleware<RequestLoggerMiddleware>();

// Örnek Test Endpointleri (Buraya gelen her request Middleware tarafından loglanır)
app.MapGet("/api/success", () => Results.Ok(new { message = "İşlem Başarılı" })); // http-200'e gider

// Bu endpoint'i manuel olarak tanımla (Middleware tarafından yakalanır)
app.MapGet("/api/not-found", ([FromServices] ILogger<Program> logger) => 
{
    logger.LogWarning("Özel 404 testi tetiklendi.");
    return Results.NotFound(new { message = "Kaynak bulunamadı" }); // http-404'e gider
}); 

// Sunucu hatası simülasyonu
app.MapGet("/api/server-error", () => Results.Problem("Sunucu patladı")); // http-500'e gider

app.Run();
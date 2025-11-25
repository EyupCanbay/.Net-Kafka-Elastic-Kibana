using KafkaProducerApi.Models;
using KafkaProducerApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Health Check
app.MapGet("/", () => new
{
    service = "Kafka Producer API",
    status = "Running",
    timestamp = DateTime.UtcNow
});

// Tek sipariş gönder
app.MapPost("/api/orders", async (OrderRequest request, IKafkaProducerService producer) =>
{
    try
    {
        var message = new OrderMessage
        {
            OrderId = Guid.NewGuid().ToString(),
            ProductName = request.ProductName,
            Quantity = request.Quantity,
            Price = request.Price,
            OrderDate = DateTime.UtcNow,
            CustomerName = request.CustomerName
        };

        var result = await producer.ProduceAsync("orders-topic", message.OrderId, message);

        return Results.Ok(new
        {
            success = true,
            message = "Sipariş Kafka'ya gönderildi",
            orderId = message.OrderId,
            topic = result.Topic,
            partition = result.Partition.Value,
            offset = result.Offset.Value
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

// Toplu sipariş gönder
app.MapPost("/api/orders/batch", async (int count, IKafkaProducerService producer) =>
{
    try
    {
        var results = new List<object>();
        var products = new[] { "Laptop", "Mouse", "Keyboard", "Monitor", "Headset" };
        var customers = new[] { "Ahmet Yılmaz", "Ayşe Demir", "Mehmet Kaya", "Fatma Şahin", "Ali Çelik" };

        for (int i = 0; i < count; i++)
        {
            var message = new OrderMessage
            {
                OrderId = Guid.NewGuid().ToString(),
                ProductName = products[Random.Shared.Next(products.Length)],
                Quantity = Random.Shared.Next(1, 10),
                Price = Random.Shared.Next(100, 5000),
                OrderDate = DateTime.UtcNow,
                CustomerName = customers[Random.Shared.Next(customers.Length)]
            };

            var result = await producer.ProduceAsync("orders-topic", message.OrderId, message);
            results.Add(new { orderId = message.OrderId, offset = result.Offset.Value });
        }

        return Results.Ok(new
        {
            success = true,
            message = $"{count} adet sipariş gönderildi",
            orders = results
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

// Log mesajı gönder
app.MapPost("/api/logs", async (LogMessage log, IKafkaProducerService producer) =>
{
    try
    {
        log.Timestamp = DateTime.UtcNow;
        log.LogId = Guid.NewGuid().ToString();

        var result = await producer.ProduceAsync("logs-topic", log.LogId, log);

        return Results.Ok(new
        {
            success = true,
            message = "Log kaydedildi",
            logId = log.LogId,
            offset = result.Offset.Value
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

app.Run();
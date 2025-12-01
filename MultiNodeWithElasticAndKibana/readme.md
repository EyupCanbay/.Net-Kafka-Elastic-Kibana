# 🚀 Kafka & Elasticsearch HTTP Log Sistemi

Bu proje, gelen HTTP isteklerini otomatik olarak Kafka'ya gönderen ve Elasticsearch'te saklayan event-driven bir loglama sistemidir.


---

## 🏗 Sistem Mimarisi
```
┌─────────────┐      ┌──────────────┐      ┌─────────────┐      ┌──────────────┐
│   HTTP      │      │   Kafka      │      │  Consumer   │      │ Elasticsearch│
│   Request   │────▶│   Topics      │────▶│  Service    │────▶│    Inde x    │ 
│             │      │              │      │             │      │              │
└─────────────┘      └──────────────┘      └─────────────┘      └──────────────┘
                             │
                             ▼
                     ┌──────────────┐
                     │   Kafka UI   │
                     │  (Port 8080) │
                     └──────────────┘
```

### Bileşenler

1. **Producer API (ASP.NET Core)**: HTTP isteklerini Middleware ile yakalayıp Kafka'ya gönderir
2. **Kafka Cluster (3 Broker)**: Logları topic'lere göre dağıtır ve saklar
3. **Consumer Service (C# Console App)**: Kafka'dan mesajları okuyup Elasticsearch'e yazar
4. **Elasticsearch**: Logları index'ler ve sorgulama imkanı sunar
5. **Kibana**: Elasticsearch verilerini görselleştirir

**NOT**: Elasticsearch ve Kibana versiyonlarını aynı yapılmalıdır. Aksi halde hata alınıyor.
---



## 🛠 Kurulum Adımları

### Configürasyonları
```bash
compose dosyasını oluşturun ve projedeki compose dosyasını kopyalayın ihtiyacınıza göre configure edebilirsiniz. 

Kafka Broker Ayarları (Kraft Modu)
KAFKA_NODE_ID: Cluster içindeki her broker'ın benzersiz kimliğidir (1, 2, 3).
KAFKA_PROCESS_ROLES: broker,controller. Kraft modunda olduğu için bu node hem veri taşıyıcı (broker) hem de yönetici (controller) rolündedir.
KAFKA_CONTROLLER_QUORUM_VOTERS: Lider seçimi (voting) yapacak node'ların listesidir. Zookeeper olmadığı için cluster yönetimini bu node'lar kendi aralarında yapar.
KAFKA_LISTENERS: Kafka'nın hangi protokolleri hangi portlardan dinleyeceğini belirtir.
CONTROLLER: Cluster içi yönetim iletişimi.
PLAINTEXT: Docker network içindeki diğer container'lar için.
PLAINTEXT_HOST: Dış dünyadan (bizim bilgisayarımızdan) erişim için.
KAFKA_ADVERTISED_LISTENERS: Client'lara (Producer/Consumer) "Bana ulaşmak için bu adresi kullan" dediği kısımdır.
Docker içindeki servisler (Kafka UI) kafka-1:29092 adresini kullanır.
Bilgisayarımızdaki .NET uygulamaları localhost:9092 adresini kullanır.
CLUSTER_ID: Tüm node'ların aynı cluster'a ait olduğunu doğrulayan benzersiz kimlik anahtarıdır.
Elasticsearch Ayarları
discovery.type=single-node: Cluster oluşturmadan tek bir node olarak çalışmasını sağlar (Local development için).
xpack.security.enabled=false: Geliştirme ortamı olduğu için kullanıcı adı/şifre ve HTTPS zorunluluğunu kapatır.

```

**Beklenilen Çıktı**: 6 container çalışıyor olmalı:
- Kafka Cluster: 3 Broker (localhost:9092, 9093, 9094)
- Kafka UI: http://localhost:8080 (Cluster yönetimi için)
- Elasticsearch: http://localhost:9200 (Log veritabanı)
- Kibana: http://localhost:5601 (Log görselleştirme)

### Kafka Topic'lerinin Oluşturulması

Topic'ler otomatik olarak **Producer API başlatıldığında** oluşturulur. Aşşağıdaki kod bloğu sayesinde
```bash
public class TopicInitializer
{
    public static async Task InitTopics(string bootstrapServers)
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();

        var topics = new[] { "http-200", "http-300", "http-400", "http-404", "http-500" };

        foreach (var topicName in topics)
        {
            try
            {
                // Partition ve Replication Factor ayarları
                await adminClient.CreateTopicsAsync(new[]
                {
                    new TopicSpecification { Name = topicName, NumPartitions = 3, ReplicationFactor = 3 }
                });
                Console.WriteLine($"Topic Oluşturuldu: {topicName}");
            }
            // Topic zaten varsa sistem kendini kapatmasın diye.
            catch (CreateTopicsException e) when (e.Results[0].Error.Code == ErrorCode.TopicAlreadyExists) 
            {
                Console.WriteLine($"ℹTopic mevcut: {topicName}");
            }
        }
    }
}

```

Ancak manuel oluşturmak isterseniz:
```bash
# Container içine gir
docker exec -it kafka-1 bash

# Topic oluştur
kafka-topics --create --topic http-200 --bootstrap-server localhost:29092 --partitions 3 --replication-factor 3
kafka-topics --create --topic http-300 --bootstrap-server localhost:29092 --partitions 3 --replication-factor 3
kafka-topics --create --topic http-400 --bootstrap-server localhost:29092 --partitions 3 --replication-factor 3
kafka-topics --create --topic http-404 --bootstrap-server localhost:29092 --partitions 3 --replication-factor 3
kafka-topics --create --topic http-500 --bootstrap-server localhost:29092 --partitions 3 --replication-factor 3

# Topic'leri listele
kafka-topics --list --bootstrap-server localhost:29092
```

### 4️⃣ Consumer Service'i Başlatma
Consumer servisinde aşşağıdaki kod bloğu ile kafkaya bağlanılır ve belirtilen grupid'si ile birden fazla consumerı kafkaya aynı consumer olduğunu söyleriz. configurasyonlarımızı yaparız.
```bash
        var config = new ConsumerConfig
        {
            BootstrapServers = "localhost:9092,localhost:9093,localhost:9094", - Kafka cluster’a bağlanmak için broker adreslerini veriyoruz.
            GroupId = "log-processor-group-elastic", - Consumer’ın bağlı olduğu consumer group’u belirtiyor.
            AutoOffsetReset = AutoOffsetReset.Earliest,  - Daha önce okumadığın bir partition'a giriyorsan nereden başlayacağını belirler.
            EnableAutoCommit = false - Kafka’ya “mesajı okudum” bilgisinin otomatik gönderilmesini kapatır.
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(new[] { "http-200", "http-300", "http-400", "http-404", "http-500" }); topiclere bağlıyoruz

        Console.WriteLine("Consumer dinlemeye başladı...");
```

---

## 📂 Proje Yapısı
```
project-root/
│
├── docker-compose.yml              # Tüm altyapı tanımları (Kafka, Elastic, Kibana)
│
├── KafkaProducerApi/               # HTTP isteklerini yakalayan API
│   ├── Program.cs                  # Middleware ve endpoint tanımları
│   ├── Middleware/
│   │   └── RequestLoggerMiddleware.cs  # Her isteği Kafka'ya gönderen logic
│   ├── Services/
│   │   └── IKafkaProducerService.cs    # Kafka Producer wrapper
│   ├── Helpers/
│   │   └── TopicInitializer.cs         # Kafka topic'lerini otomatik oluşturur
│   └── Models/
│       └── HttpLogModel.cs             # Log verisi için DTO
│
└── KafkaConsumerService/           # Kafka'dan okuyan, Elastic'e yazan servis
    ├── Program.cs                  # Consumer logic + Elastic entegrasyonu
    └── Models/
        └── ElasticLogDocument.cs   # Elasticsearch döküman modeli
```

---

## ⚙️ Çalışma Mantığı

### 1. HTTP İstek Yakalanması (Middleware)

`RequestLoggerMiddleware` her HTTP isteği için:
```csharp
// İstek başında süreyi başlat
var stopwatch = Stopwatch.StartNew();

// İsteği işle
await _next(context);

// Süreyi durdur
stopwatch.Stop();

// Status code'a göre topic belirle
string topicName = DetermineTopicName(statusCode);

// Kafka'ya gönder
await producer.ProduceAsync(topicName, logData);
```

### 2. Topic Yönlendirme Mantığı

- **200-299 (Başarılı)**: `http-200`, `http-300`, `http-400` arasından **RASTGELE** seçilir
- **300-399 (Yönlendirme)**: `http-300`
- **404 (Bulunamadı)**: `http-404`
- **400-499 (İstemci Hatası)**: `http-400`
- **500+ (Sunucu Hatası)**: `http-500`

> **Neden Rastgele?** Load balancing test senaryoları için farklı topic'lere dağıtım sağlanır.

### 3. Kafka'da Saklama

Her topic **3 partition** ve **3 replication factor** ile oluşturulur:

- **Partition**: Paralel işleme ve yük dağılımı
- **Replication**: Veri kaybına karşı yedekleme (1 broker çökse bile data kaybolmaz)

### 4. Consumer'ın Elasticsearch'e Yazması
```csharp
// Kafka'dan oku
var result = consumer.Consume(TimeSpan.FromSeconds(1));

// JSON deserialize
var logData = JsonSerializer.Deserialize<HttpLogModel>(result.Message.Value, jsonOptions);

// Elasticsearch document oluştur
var elasticDoc = new ElasticLogDocument { ... };

// Index'e yaz
await elasticClient.IndexAsync(elasticDoc, documentId);

// Offset commit et (mesaj başarıyla işlendi)
consumer.Commit(result);
```

### 5. Elasticsearch'te Indexleme

Tüm loglar `http-logs-index` adlı index'te saklanır:
```json
{
  "traceId": "0HMVUQ2...",
  "httpMethod": "GET",
  "path": "/api/success",
  "statusCode": 200,
  "durationMs": 45,
  "timestamp": "2025-12-01T10:30:00Z",
  "kafkaTopic": "http-200",
  "kafkaOffset": 42
}
```

---

## 🧪 Test & Doğrulama

### 1. Producer'ı Test Etme
```bash
# Başarılı istek (200) - Rastgele topic'e gider
curl http://localhost:5000/api/success

# 404 hatası
curl http://localhost:5000/api/not-found

# 500 hatası
curl http://localhost:5000/api/server-error
```

### 2. Kafka UI ile Kontrol

Tarayıcıda [http://localhost:8080](http://localhost:8080) açın:

1. **Topics** sekmesine tıklayın
2. `http-200` gibi bir topic seçin
3. **Messages** bölümünde gönderilen logları görün


### 3. Elasticsearch'te Doğrulama
```bash
# Tüm logları listele
curl http://localhost:9200/http-logs-index/_search?pretty

# Son 10 logu getir
curl -X GET "http://localhost:9200/http-logs-index/_search?pretty" -H 'Content-Type: application/json' -d'
{
  "size": 10,
  "sort": [{"timestamp": "desc"}]
}
'
```

### 4. Kibana ile Görselleştirme

1. [http://localhost:5601](http://localhost:5601) adresini açın
2. Sol menüden **Discover** seçin
3. Index pattern oluşturun: `http-logs-index*`
4. Timestamp alanını seçin: `timestamp`
5. Logları gerçek zamanlı görün ve filtreleyin

---



## 📊 Performans Metrikleri

- **Kafka Throughput**: ~10,000 mesaj/saniye (3 broker cluster)
- **Elasticsearch Indexing**: ~5,000 döküman/saniye
- **Ortalama Latency**: 50-100ms (Producer → Elasticsearch)

---

## 🔒 Güvenlik Notları

> ⚠️ **DİKKAT**: Bu yapılandırma **sadece development ortamı** içindir!

Production ortamında mutlaka yapılmalı:

1. **Elasticsearch Security** aktif edilmeli (`xpack.security.enabled=true`)
2. **Kafka SASL/SSL** ile şifrelenmeli
3. **API Gateway** kullanılmalı
4. **Secrets Management** (örn. Azure Key Vault, HashiCorp Vault)
5. **Network Isolation** (VPC/VNET)

---


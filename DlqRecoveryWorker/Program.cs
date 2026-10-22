
using RabbitMQ.Client;
using RabbitMq.Shared.Models;
using System.Text;
using System.Text.Json;

// RabbitMQ bağlantı ayarları.
var factory = new ConnectionFactory
{
    HostName = "localhost"
};

// RabbitMQ bağlantısını oluştur.
await using var connection =
    await factory.CreateConnectionAsync();

// Mesajları güvenli şekilde yeniden yayınlamak için
// Publisher Confirm özelliğini etkinleştir.
var options = new CreateChannelOptions(
    publisherConfirmationsEnabled: true,
    publisherConfirmationTrackingEnabled: true
);

await using var channel =
    await connection.CreateChannelAsync(options);

int recoveredCount = 0;
int invalidCount = 0;

Console.WriteLine("DLQ kurtarma uygulaması hazır.");
Console.WriteLine("Geçerli mesajlar database_queue'ya aktarılacak.");
Console.WriteLine("Bozuk mesajlar DLQ'da korunacak.");
Console.WriteLine();

// DLQ'nun başlangıçtaki mesaj sayısını öğren.
var queueInfo = await channel.QueueDeclarePassiveAsync(
    "database_queue_dlq"
);

uint messageCount = queueInfo.MessageCount;

Console.WriteLine($"DLQ mesaj sayısı: {messageCount}");
Console.WriteLine();

Console.Write("Kurtarmayı başlatmak için EVET yazın: ");

if (Console.ReadLine() != "EVET")
{
    Console.WriteLine("İşlem iptal edildi.");
    return;
}

// Başlangıçtaki mesaj sayısı kadar kontrol yap.
// Böylece bozuk mesajlar nedeniyle sonsuz döngü oluşmaz.
for (uint i = 0; i < messageCount; i++)
{
    var result = await channel.BasicGetAsync(
        queue: "database_queue_dlq",
        autoAck: false
    );

    if (result == null)
        break;

    SyntheticOrderMessage? order;

    try
    {
        string json = Encoding.UTF8.GetString(
            result.Body.ToArray()
        );

        // JSON mesajını sipariş modeline dönüştür.
        order = JsonSerializer.Deserialize<SyntheticOrderMessage>(json);

        if (order == null || order.MessageId == Guid.Empty)
        {
            throw new JsonException("Geçersiz sipariş.");
        }
    }
    catch (JsonException)
    {
        invalidCount++;

        Console.WriteLine(
            "GEÇERSİZ MESAJ | DLQ'da korunacak."
        );

        // ACK veya NACK göndermiyoruz.
        // Kanal kapanınca mesaj yeniden DLQ'ya dönecek.
        continue;
    }

    try
    {
        // Eski Retry başlıklarını taşımıyoruz.
        // Kurtarılan mesaj yeni bir işleme döngüsüne başlar.
        var properties = new BasicProperties
        {
            Persistent = true
        };

        // Geçerli mesajı ana kuyruğa yeniden yayınla.
        await channel.BasicPublishAsync(
            exchange: "",
            routingKey: "database_queue",
            mandatory: true,
            basicProperties: properties,
            body: result.Body
        );

        // Yayın onayı geldikten sonra eski mesajı ACK ile kaldır.
        await channel.BasicAckAsync(
            deliveryTag: result.DeliveryTag,
            multiple: false
        );

        recoveredCount++;

        Console.WriteLine(
            $"KURTARILDI | MessageId: {order.MessageId}"
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"KURTARMA HATASI | {ex.Message}"
        );

        // Yayın veya ACK başarısızsa işlemi durdur.
        // Onaylanmamış mesajlar bağlantı kapanınca DLQ'ya döner.
        break;
    }
}

Console.WriteLine();
Console.WriteLine("------- KURTARMA SONUCU -------");
Console.WriteLine($"Kurtarılan mesaj: {recoveredCount}");
Console.WriteLine($"Geçersiz mesaj: {invalidCount}");
Console.WriteLine("Kurtarma işlemi tamamlandı.");

using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMqConsumer.Models;
using System.Text;
using System.Text.Json;

var factory = new ConnectionFactory()    // bağlantı oluşturuyoruz localimizde
{
    HostName = "localhost"
};

using var connection =
    await factory.CreateConnectionAsync();   // Bağlantı ve kanal ayarları

using var channel =
    await connection.CreateChannelAsync();

var arguments = new Dictionary<string, object?>   // normal queue’daki başarısız mesaj nereye gitsin?
{
    { "x-dead-letter-exchange", "" },
    { "x-dead-letter-routing-key", "hello_queue_dlq" }
};

await channel.QueueDeclareAsync(         // producer ile consumer aynı queue özelliklerini kullanmalı
    queue: "hello_queue",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: arguments
);

await channel.QueueDeclareAsync(
    queue: "hello_queue_dlq",            // dlq oluşturup sadece başarısız mesajları gönderdiğimiz kısım
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);

var consumer =
    new AsyncEventingBasicConsumer(channel); // bu consumer nesnesinin görevi queue ye gelen mesajları dinlemek

consumer.ReceivedAsync += async (model, ea) => // rabbitmq dan yeni mesaj geldiğinde bu event tetikleniyor model=consumer nesnesi ea=gelen mesajla ilgili bilgileri taşıyan nesne
{
    try
    {
        var body = ea.Body.ToArray();         // ea.Body = rabbitmq dan gelen mesajın byte hali

        var message =
            Encoding.UTF8.GetString(body);    // byte tipi mesajı stringe dönüştürür

        var order =
            JsonSerializer.Deserialize<OrderMessage>(message); // rabbitmq dan gelen metini tekrar nesneye çevirir

        if (order == null)
        {
            throw new InvalidOperationException(
                "OrderMessage deserialize edilemedi.");
        }

        Console.WriteLine(
            $"Order Id: {order.OrderId}");

        Console.WriteLine(
            $"User Id: {order.UserId}");

        Console.WriteLine(
            $"Total: {order.Total}");

        await channel.BasicAckAsync(
            deliveryTag: ea.DeliveryTag,      // RabbitMQ Consumer’a bir mesaj gönderdiğinde o teslimata bir numara verir=DeliveryTag
            multiple: false                   // yalnızca o numaralı teslimatı onaylarız
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"Hata Oluştu: {ex.Message}");

        await channel.BasicNackAsync(
            deliveryTag: ea.DeliveryTag,
            multiple: false,
            requeue: false
        );
    }
};

await channel.BasicConsumeAsync(
    queue: "hello_queue",
    autoAck: false,                          // consumer mesajı aldığı anda rabbitmq mesajı işlenmiş kabul etmez
    consumer: consumer
);

Console.WriteLine(
    "Consumer mesaj bekliyor...");

await Task.Delay(
    Timeout.Infinite);                       // consumerın sürekli açık kalıp queue yi dinlemesini istiyoruz
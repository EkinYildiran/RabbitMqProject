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

await channel.ExchangeDeclareAsync(
    exchange: "order_exchange",  // exchange in adı 
    type: ExchangeType.Direct,   // direct exchange kullanıyoruz
    durable: true                //Rabbitmq yeniden başlasa da exchange tanımı kalsın
    );

await channel.QueueDeclareAsync(         // producer ile consumer aynı queue özelliklerini kullanmalı
    queue: "hello_queue",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: arguments
);

await channel.QueueBindAsync(     //binding kodumuz
    queue: "hello_queue",
    exchange: "order_exchange",
    routingKey: "order.created"
);

await channel.QueueDeclareAsync(     // ikinci bir queue oluşturuyoruz
    queue: "notification_queue",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);

await channel.QueueBindAsync(
    queue: "notification_queue",
    exchange: "order_exchange",
    routingKey: "order.notification"
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

var notificationConsumer = new AsyncEventingBasicConsumer(channel);  // ikinci bir consumer 

consumer.ReceivedAsync += async (model, ea) => // rabbitmq dan yeni mesaj geldiğinde bu event tetikleniyor model=consumer nesnesi ea=gelen mesajla ilgili bilgileri taşıyan nesne
{
    bool success = false;

    var body = ea.Body.ToArray();         // ea.Body = rabbitmq dan gelen mesajın byte hali

    var message =
        Encoding.UTF8.GetString(body);    // byte tipi mesajı stringe dönüştürür

    var order =
        JsonSerializer.Deserialize<OrderMessage>(message); // rabbitmq dan gelen metini tekrar nesneye çevirir

    // Mesaj OrderMessage nesnesine dönüştürülemediyse retry yapmanın anlamı yok
    if (order == null)
    {
        Console.WriteLine(
            "OrderMessage deserialize edilemedi.");

        await channel.BasicNackAsync(
            deliveryTag: ea.DeliveryTag,
            multiple: false,
            requeue: false
        );

        return;
    }

    Console.WriteLine(
        $"Order Id: {order.OrderId}");

    Console.WriteLine(
        $"User Id: {order.UserId}");

    Console.WriteLine(
        $"Total: {order.Total}");

    // Mesajın işlenmesini en fazla 3 kez deniyoruz
    for (int i = 1; i <= 3; i++)
    {
        try
        {
            Console.WriteLine($"{i}. deneme");

            await ProcessOrderAsync(order);

            // Buraya geldiysek ProcessOrderAsync hata vermedi
            success = true;

            // İşlem başarılı olduğu için tekrar denemeye gerek yok
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"{i}. deneme başarısız: {ex.Message}");

            if (i < 3)
            {
                await Task.Delay(2000);
            }
        }
    }

    // 3 denemeden biri başarılı olduysa mesajı onaylıyoruz
    if (success)
    {
        await channel.BasicAckAsync(
            deliveryTag: ea.DeliveryTag,      // RabbitMQ Consumer’a bir mesaj gönderdiğinde o teslimata bir numara verir=DeliveryTag
            multiple: false                   // yalnızca o numaralı teslimatı onaylarız
        );

        Console.WriteLine("Mesaj başarıyla işlendi ve ACK gönderildi.");
    }
    else
    {
        // 3 denemenin tamamı başarısız olduysa mesajı tekrar ana queue'ya koymuyoruz
        // Dead Letter Queue ayarı sayesinde mesaj DLQ'ya gönderilecek
        await channel.BasicNackAsync(
            deliveryTag: ea.DeliveryTag,
            multiple: false,
            requeue: false
        );

        Console.WriteLine(
            "3 deneme de başarısız oldu. Mesaj DLQ'ya gönderildi.");
    }
};

notificationConsumer.ReceivedAsync += async (model, ea) =>
{
    var body = ea.Body.ToArray();
    var message = Encoding.UTF8.GetString(body);

    var order = JsonSerializer.Deserialize<OrderMessage>(message);

    if (order == null)
    {
        await channel.BasicNackAsync(
            deliveryTag: ea.DeliveryTag,
            multiple: false,
            requeue: false
        );

        return;
    }

    Console.WriteLine(
        $"Notification mesajı alındı. OrderId: {order.OrderId}");

    await channel.BasicAckAsync(
        deliveryTag: ea.DeliveryTag,
        multiple: false
    );
};

static Task ProcessOrderAsync(OrderMessage order)
{
    if (order.Total <= 0)
    {
        throw new InvalidOperationException(
            "Total değeri 0'dan küçük veya eşit olamaz");
    }

    Console.WriteLine("Sipariş Başarıyla İşlendi");

    return Task.CompletedTask;
}

await channel.BasicQosAsync(
    prefetchSize: 0,   // byte bazlı limit kullanmıyoruz    
    prefetchCount: 1,  // consumer aynı anda en fazla 1 tane ack bekleyen mesaj alsın
    global: false      // ilgili consumer/ channel ayarı için kullanılır
);

await channel.BasicConsumeAsync(
    queue: "hello_queue",
    autoAck: false,                          // consumer mesajı aldığı anda rabbitmq mesajı işlenmiş kabul etmez
    consumer: consumer
);

await channel.BasicConsumeAsync(
    queue: "notification_queue",
    autoAck: false,
    consumer: notificationConsumer
);

Console.WriteLine(
    "Consumer mesaj bekliyor...");

await Task.Delay(
    Timeout.Infinite);                       // consumerın sürekli açık kalıp queue yi dinlemesini istiyoruz
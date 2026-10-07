using RabbitMQ.Client;
using RabbitMqProducer.Models;
using System.Text;
using System.Text.Json;

var factory = new ConnectionFactory()   // Rabbitmq bağlantı ayarları oluşturulur
{
    HostName = "localhost"
};

var properties = new BasicProperties    // mesajın kalıcı hale gelmesini sağlayan nesne
{
    Persistent = true
};

using var connection =
    await factory.CreateConnectionAsync(); // RabbitMQ gerçek bir bağlantıyı açıyoruz (ana bağlantı)

using var channel =
    await connection.CreateChannelAsync(); // Channel oluşturuyoruz (ana bağlantı üzerindeki işlem yapılan kanal)

var order = new OrderMessage
{
    OrderId = 1,
    UserId = 25,
    Total = 1500
};

string json =
    JsonSerializer.Serialize(order);

var body =
    Encoding.UTF8.GetBytes(json);

await channel.BasicPublishAsync(
    exchange: "",                       // varsayılan exchange
    routingKey: "hello_queue",          // varsayılan exchange varsa kuyruğa hello_queue adını veriyoruz
    mandatory: false,                   // gönderdiğin mesaj uygun bir queue’ya yönlendirilemezse ne olacağını belirler
    basicProperties: properties,        // mesajın kalıcı hale gelmesi için gereken nesneyi publish ettik durable=queue persistent=mesaj için
    body: body
);

Console.WriteLine(
    $"Mesaj gönderildi: {json}");
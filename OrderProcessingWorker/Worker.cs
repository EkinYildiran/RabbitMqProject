
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMq.Shared.Models;
using System.Text;
using System.Text.Json;

namespace OrderProcessingWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;

        public Worker(
            ILogger<Worker> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string consumerId =_configuration["ConsumerSettings:ConsumerId"] ?? "Consumer-1";
            // RabbitMQ sunucusuna bağlantı ve mesajlaşma kanalları oluşturulur.
            var factory = new ConnectionFactory
            {
                HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST")
                     ?? "localhost"
            };

            await using var connection =
                await factory.CreateConnectionAsync(stoppingToken);

            await using var channel =
                await connection.CreateChannelAsync(
                    cancellationToken: stoppingToken);

            // İkinci kuyruğa mesaj göndermek için yayın onaylı ayrı bir kanal oluşturulur.
            var options = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            );

            await using var publishChannel =
                await connection.CreateChannelAsync(
                    options: options,
                    cancellationToken: stoppingToken);

            // Başarısız sipariş mesajlarının saklanacağı kuyruk.
            await channel.QueueDeclareAsync(
                queue: "order_queue_dlq",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken
            );

            // Siparişlerin alınacağı ilk kuyruk hazırlanır.
            await channel.QueueDeclareAsync(
                queue: "order_queue",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    { "x-dead-letter-exchange", "" },  //reddedilen mesaj hangi exchange üzerinden gelir
                    { "x-dead-letter-routing-key", "order_queue_dlq" }  // mesajın yönlendirileceği hata kodunun adıdır
                },
                cancellationToken: stoppingToken
            );

            // İşlenen siparişlerin veritabanına aktarılmak üzere bekleneceği kuyruk hazırlanır.
            await channel.QueueDeclareAsync(
                queue: "database_queue",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    { "x-dead-letter-exchange", "" },
                    { "x-dead-letter-routing-key", "database_queue_dlq" }
                },
                cancellationToken: stoppingToken
            );

            // Consumer'ın aynı anda en fazla 1 onaylanmamış mesaj alması sağlanır.
            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 1,
                global: false,
                cancellationToken: stoppingToken
            );

            // RabbitMQ'dan gelen mesajları dinleyecek Consumer oluşturulur.
            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                SyntheticOrderMessage? order;

                // 1. Mesajın geçerli olup olmadığı kontrol edilir.
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    order = JsonSerializer.Deserialize<SyntheticOrderMessage>(json);

                    if (order == null)
                        throw new JsonException("Sipariş verisi boş.");
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Geçersiz sipariş mesajı.");

                    // Geçersiz mesaj DLQ'ya yönlendirilir.
                    await channel.BasicNackAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        requeue: false
                    );

                    return;
                }

                // 2. Geçerli mesaj ikinci kuyruğa aktarılır.
                try
                {
                    var properties = new BasicProperties
                    {
                        Persistent = true
                    };

                    await publishChannel.BasicPublishAsync(
                        exchange: "",
                        routingKey: "database_queue",
                        mandatory: true,
                        basicProperties: properties,
                        body: ea.Body,
                        cancellationToken: stoppingToken
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "database_queue aktarımı başarısız.");

                    // Geçici hata: Mesaj ana kuyruğa geri alınır.
                    await channel.BasicNackAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        requeue: true
                    );

                    return;
                }

                // 3. Yayın onayından sonra ilk mesaj ACK ile tamamlanır.
                try
                {
                    await channel.BasicAckAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false
                    );

                    _logger.LogInformation(
                    "{ConsumerId} - Sipariş {OrderId} database_queue kuyruğuna aktarıldı.",
                    consumerId,
                    order.OrderId
                );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Mesaj ACK işlemi başarısız.");

                    // Bu durumda ikinci kez ACK veya NACK gönderilmez.
                }
            };

            // Consumer, order_queue kuyruğunu manuel ACK ile dinlemeye başlar.
            await channel.BasicConsumeAsync(
                queue: "order_queue",
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken
            );

            // Uygulama durdurulana kadar Consumer ve bağlantılar açık tutulur.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}

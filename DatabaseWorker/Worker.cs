
using DatabaseWorker.Data;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMq.Shared.Models;
using System.Text;
using System.Text.Json;
using DatabaseWorker.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace DatabaseWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public Worker(
            ILogger<Worker> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // RabbitMQ sunucusuna ba�lan�l�r.
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

            // Retry mesajlar� i�in ayr� Publisher kanal� olu�turulur.
            await using var retryChannel =
                await connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true
                    ),
                    cancellationToken: stoppingToken
                );

            // Veritaban�na kaydedilemeyen ge�ersiz mesajlar�n kuyru�u.
            await channel.QueueDeclareAsync(
                queue: "database_queue_dlq",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken
            );

            // Veritaban�na kaydedilecek mesajlar�n bulundu�u kuyruk haz�rlan�r.
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

            // Ayn� anda en fazla 1 onaylanmam�� mesaj al�n�r.
            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 1,
                global: false,
                cancellationToken: stoppingToken
            );

            // Mesajlar� dinleyecek Consumer olu�turulur.
            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                // RabbitMQ mesaj�n� C# nesnesine d�n��t�r.
                SyntheticOrderMessage? order;

                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    order = JsonSerializer.Deserialize<SyntheticOrderMessage>(json);

                    if (order == null)
                        throw new JsonException("Sipari� verisi bo�.");
                }
                catch (JsonException ex)
                {
                    _logger.LogError(
                        ex,
                        "Ge�ersiz sipari� mesaj�. DLQ'ya g�nderiliyor.");

                    await channel.BasicNackAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        requeue: false
                    );

                    return;
                }

                // RabbitMQ mesaj ba�l���ndan Retry say�s�n� oku.
                int retryCount = 0;

                if (ea.BasicProperties.Headers != null &&
                    ea.BasicProperties.Headers.TryGetValue(
                        "x-retry-count", out var value))
                {
                    // RabbitMQ'dan gelen ba�l�k farkl� veri tiplerinde olabilir.
                    retryCount = value switch
                    {
                        byte number => number,
                        short number => number,
                        int number => number,
                        long number => checked((int)number),

                        byte[] bytes when int.TryParse(
                            Encoding.UTF8.GetString(bytes),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int number) => number,

                        _ => 0
                    };
                }

                // Mesaj�n hangi denemede oldu�unu g�ster.
                _logger.LogWarning(
                    "RETRY KONTROL | MessageId: {MessageId} | Deneme: {Attempt}/3",
                    order.MessageId,
                    retryCount + 1
                );

                try
                {
                    // Her mesaj i�in ayr� bir DbContext olu�tur.
                    await using var scope = _scopeFactory.CreateAsyncScope();

                    var dbContext = scope.ServiceProvider
                        .GetRequiredService<AppDbContext>();
                    
                    // Mesaj daha �nce kaydedilmi� mi?
                    bool exists = await dbContext.Orders
                        .AnyAsync(
                            x => x.MessageId == order.MessageId,
                            stoppingToken
                        );

                    if (exists)
                    {
                        _logger.LogInformation(
                            "Mesaj zaten kay�tl�: {MessageId}",
                            order.MessageId
                        );
                    }
                    else
                    {
                        // Gelen mesaj� veritaban� kayd�na d�n��t�r.
                        var record = new OrderRecord
                        {
                            MessageId = order.MessageId,
                            OrderId = order.OrderId,
                            UserId = order.UserId,
                            Total = order.Total,
                            CreatedAt = order.CreatedAt,
                            WorkerId = order.WorkerId
                        };

                        dbContext.Orders.Add(record);

                        await dbContext.SaveChangesAsync(stoppingToken);

                        _logger.LogInformation(
                            "Sipari� SQL Server'a kaydedildi: {OrderId}",
                            order.OrderId
                        );
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "SQL HATASI | MessageId: {MessageId} | Deneme: {Attempt}/3 | Hata: {Error}",
                        order.MessageId,
                        retryCount + 1,
                        ex.GetBaseException().Message
                    );

                    // 3. ba�ar�s�zl�kta mesaj DLQ'ya g�nderilir.
                    if (retryCount >= 2)
                    {
                        _logger.LogWarning(
                            "DLQ'YA G�NDER�L�YOR | MessageId: {MessageId}",
                            order.MessageId
                        );

                        await channel.BasicNackAsync(
                            deliveryTag: ea.DeliveryTag,
                            multiple: false,
                            requeue: false
                        );

                        return;
                    }

                    // Yeniden denemeden �nce 2 saniye beklenir.
                    await Task.Delay(2000, stoppingToken);

                    try
                    {
                        // �nceki ba�l�klar� koru, deneme say�s�n� art�r.
                        var headers = ea.BasicProperties.Headers != null
                            ? new Dictionary<string, object?>(
                                ea.BasicProperties.Headers)
                            : new Dictionary<string, object?>();

                        // Sayac� UTF-8 byte dizisi olarak g�nderiyoruz.
                        // B�ylece RabbitMQ'dan okurken ayn� bi�imi kullanabiliriz.
                        int newRetryCount = retryCount + 1;

                        headers["x-retry-count"] =
                            Encoding.UTF8.GetBytes(
                                newRetryCount.ToString(
                                    CultureInfo.InvariantCulture)
                            );

                        var properties = new BasicProperties
                        {
                            Persistent = true,
                            Headers = headers
                        };

                        _logger.LogWarning(
                            "RETRY YAYINI BA�LIYOR | MessageId: {MessageId} | Yeni saya�: {Count}",
                            order.MessageId,
                            newRetryCount
                        );

                        // Ba�ar�s�z mesaj yeniden database_queue'ya yay�nlan�r.
                        await retryChannel.BasicPublishAsync(
                            exchange: "",
                            routingKey: "database_queue",
                            mandatory: true,
                            basicProperties: properties,
                            body: ea.Body,
                            cancellationToken: stoppingToken
                        );

                        _logger.LogWarning(
                            "RETRY YAYINI TAMAMLANDI | MessageId: {MessageId}",
                            order.MessageId
                        );

                        // Yeni mesaj kabul edildi; eski teslimat� onayla.
                        await channel.BasicAckAsync(
                            deliveryTag: ea.DeliveryTag,
                            multiple: false
                        );
                    }
                    catch (Exception publishEx)
                        when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(
                            publishEx,
                            "Retry yay�n� veya ACK ba�ar�s�z. Orijinal mesaj onaylanamad�."
                        );

                        // Orijinal teslimata burada ACK/NACK g�ndermiyoruz.
                        // Ba�lant� kapan�rsa RabbitMQ onaylanmam�� mesaj� yeniden kuyru�a al�r.
                        throw;
                    }

                    return;
                }

                // SQL kayd� ba�ar�l�ysa veya mesaj zaten kay�tl�ysa ACK g�nder.
                await channel.BasicAckAsync(
                    deliveryTag: ea.DeliveryTag, //RabbitMQ'nun bu kanal �zerindeki mesaj teslimat�na verdi�i numarad�r.
                    multiple: false // sadece bu mesaj� onaylar
                );
            };

            await channel.BasicConsumeAsync(
                queue: "database_queue",
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken
            );

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}

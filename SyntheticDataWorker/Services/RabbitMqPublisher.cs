
using RabbitMq.Shared.Models;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace SyntheticDataWorker.Services
{
    public class RabbitMqPublisher : IAsyncDisposable
    {
        private readonly ConnectionFactory _factory; //ConnectionFactory=bağlantı ayarları   _factory= bizim nesne
        private IConnection? _connection; // Bağlantı henüz açılmadığında null olabilir

        private IChannel? _channel; // Mesajları göndermek için kullanılacak ortak kanal

        private readonly SemaphoreSlim _connectionLock = new(1, 1); // aynı anda birden fazla işlem bağlantı oluşturmaya çalışırsa bunları sıraya koyar

        // Aynı kanal üzerinden eş zamanlı yayın yapılmasını engeller.
        private readonly SemaphoreSlim _publishLock = new(1, 1);

        public RabbitMqPublisher()
        {
            _factory = new ConnectionFactory
            {
                HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST")
                     ?? "localhost"
            };
        }

        public async Task PublishAsync(
            SyntheticOrderMessage order,
            CancellationToken cancellationToken)
        {
            // Kanal daha önce InitializeAsync ile hazırlanmış olmalı.
            if (_channel is null || !_channel.IsOpen)
            {
                throw new InvalidOperationException(
                    "RabbitMQ yayın kanalı hazır değil.");
            }

            string json = JsonSerializer.Serialize(order);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            var properties = new BasicProperties
            {
                Persistent = true //mesaj kalıcı olarak saklansın
            };

            await _publishLock.WaitAsync(cancellationToken);

            try
            {
                await _channel.BasicPublishAsync(
                    exchange: "", //rabbitmq nun varsayılan exchangeini kullanır
                    routingKey: "order_queue", //mesajı bu isimdeki kuyruğa yönlendir
                    mandatory: true, //mesaj yönlendirilmezse yayıncıya geri gelsin
                    basicProperties: properties,
                    body: jsonBytes, //Gönderilecek mesjaın içeriğidirç
                    cancellationToken: cancellationToken
                );
            }
            finally
            {
                _publishLock.Release();
            }
        }

        public async Task InitializeAsync( // uygulama başlarken önce gerekli rabbitmq hazırlıklarını yapmak
            CancellationToken cancellationToken)
        {
            var connection = await GetConnectionAsync(cancellationToken);

            // Kanalı sadece bir kez oluştur.
            // Publisher Confirm özelliği açık kalır.
            var options = new CreateChannelOptions(
                publisherConfirmationsEnabled: true, // rabbitmq dan yayın onayı ister
                publisherConfirmationTrackingEnabled: true //RabbitMQClient kütüphanesinin yayın onaylarını yakip etmesini sağlar
            );

            _channel = await connection.CreateChannelAsync(
                options: options,
                cancellationToken: cancellationToken
            );

            await _channel.QueueDeclareAsync(
                queue: "order_queue",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    { "x-dead-letter-exchange", "" },
                    { "x-dead-letter-routing-key", "order_queue_dlq" }
                },
                cancellationToken: cancellationToken);
        }

        private async Task<IConnection> GetConnectionAsync(
            CancellationToken cancellationToken)
        {
            if (_connection is { IsOpen: true }) // bağlantı varsa kullan
                return _connection;

            await _connectionLock.WaitAsync(cancellationToken);

            try // kilidi aldıktan sonra bağlantıyı tekrar kontrol et
            {
                if (_connection is not { IsOpen: true })
                {
                    _connection = await _factory.CreateConnectionAsync(
                        cancellationToken);
                }

                return _connection;
            }
            finally
            {
                _connectionLock.Release(); // en son kilidi serbest bırak
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Önce kanalı, ardından bağlantıyı kapat.
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            _publishLock.Dispose();
            _connectionLock.Dispose();
        }
    }
}

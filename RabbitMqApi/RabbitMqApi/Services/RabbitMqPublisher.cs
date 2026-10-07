using RabbitMQ.Client;
using RabbitMqApi.Interfaces;
using RabbitMqApi.Models;
using System.Text;
using System.Text.Json;

namespace RabbitMqApi.Services
{
    public class RabbitMqPublisher : IRabbitMqPublisher, IAsyncDisposable
    {
        private readonly IConfiguration _configuration;

        private IConnection? _connection;

        private readonly SemaphoreSlim _connectionLock = new(1, 1);   // aynı anda birden fazla işlemin aynı kritik bölgeye girmesini sınırlayan bir senkronizasyon aracı

        private readonly ILogger<RabbitMqPublisher> _logger;

        public RabbitMqPublisher(
            IConfiguration configuration,
            ILogger<RabbitMqPublisher> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private async Task<IConnection> GetConnectionAsync()   // bana kullanılabilir bir rabbitmq connection ver
        {
            if (_connection != null && _connection.IsOpen)
            {
                return _connection;                            // connection varsa ve gerçekten açıksa connectionu döndür
            }

            await _connectionLock.WaitAsync();                 // connection oluşturma bölümüne aynı anda sadece bir request girsin

            try
            {
                if (_connection != null && _connection.IsOpen) // double-check
                {
                    return _connection;
                }

                var hostName = _configuration["RabbitMQ:HostName"]
                    ?? throw new InvalidOperationException(
                        "RabbitMQ HostName ayarı bulunamadı.");

                var factory = new ConnectionFactory            // hala bir connection yoksa yenisini oluşturuyoruz
                {
                    HostName = hostName
                };

                _connection = await factory.CreateConnectionAsync();

                return _connection;
            }
            finally
            {
                _connectionLock.Release();                     // Ben işimi bitirdim başka request varsa içeri girebilir
            }
        }

        public async Task PublishAsync(OrderMessage order)     // dışarıdan bir order alır metodun adı da publish
        {
            try
            {
                var connection = await GetConnectionAsync();   // gerçek bağlantıyı alıyoruz

                using var channel =
                    await connection.CreateChannelAsync();     // bağlantı üzerinden mesaj göndereceğimiz bir channel açıyoruz

                var queueName = _configuration["RabbitMQ:QueueName"]
                    ?? throw new InvalidOperationException(
                        "RabbitMQ QueueName ayarı bulunamadı.");

                var json = JsonSerializer.Serialize(order);    // ordermessage nesnesini jsona çeviriyoruz

                var body = Encoding.UTF8.GetBytes(json);       // json stringi byte a çeviriyoruz

                var properties = new BasicProperties           // mesajın persistent(kalıcı) olmasını istiyoruz
                {
                    Persistent = true
                };

                await channel.BasicPublishAsync(               // yayınlama kısmı
                    exchange: "",
                    routingKey: queueName,
                    mandatory: false,
                    basicProperties: properties,
                    body: body
                );

                _logger.LogInformation(
                    "RabbitMQ mesajı gönderildi. OrderId: {OrderId}",
                    order.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "RabbitMQ mesajı gönderilemedi. OrderId: {OrderId}",
                    order.OrderId);

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync();              // rabbitmq bağlantısını koparıyor
                _connection = null;
            }

            _connectionLock.Dispose();                         // SemaphoreSlim nesnesini temizliyor
        }
    }
}
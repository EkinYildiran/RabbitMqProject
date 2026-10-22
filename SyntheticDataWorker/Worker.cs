using Microsoft.Extensions.Configuration;
using RabbitMq.Shared.Models;
using SyntheticDataWorker.Services;

namespace SyntheticDataWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly RabbitMqPublisher _publisher;
        private readonly IConfiguration _configuration; // appsettings den ayarları okumasını sağlar 

        public Worker(
         ILogger<Worker> logger,
         RabbitMqPublisher publisher,
         IConfiguration configuration)
        {
            _logger = logger;
            _publisher = publisher;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _publisher.InitializeAsync(stoppingToken);//RabbitMQ bağlantısını ve kuyruğu hazırla. Hazırlık bitince ben sipariş üretmeye başlayacağım. Uygulama kapatılırsa iptal isteğini de dikkate al.

            string workerId = _configuration["WorkerSettings:WorkerId"] ?? "Worker-1";   //?? "Worker-1" → Okunan değer null ise varsayılan olarak "Worker-1" kullanır.
            int delay = _configuration.GetValue<int>("WorkerSettings:DelayMilliseconds", 100);

            while (!stoppingToken.IsCancellationRequested)
            {
                var order = new SyntheticOrderMessage
                {
                    MessageId = Guid.NewGuid(), // guid her mesaj için benzersiz kimlik oluşturur 
                    OrderId = Random.Shared.Next(1, 100001), // random.shared.next rastgele sipariş bilgileri üretiyor
                    UserId = Random.Shared.Next(1, 1001),
                    Total = Random.Shared.Next(100, 10001),
                    CreatedAt = DateTime.UtcNow,
                    WorkerId = workerId   // mesajın hangi worker tarafından üretildiğini gösteriyor
                };
                await _publisher.PublishAsync(order, stoppingToken);
                _logger.LogInformation(
                    "Sipariş: {OrderId}, Tutar: {Total}",
                    order.OrderId,
                    order.Total
                );
                await Task.Delay(delay, stoppingToken);   
            }
        }

    }
}

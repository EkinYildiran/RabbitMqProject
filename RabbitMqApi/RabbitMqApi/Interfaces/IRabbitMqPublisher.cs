using RabbitMqApi.Models;

namespace RabbitMqApi.Interfaces;

public interface IRabbitMqPublisher
{
    Task PublishAsync(OrderMessage order, string routingKey);
}
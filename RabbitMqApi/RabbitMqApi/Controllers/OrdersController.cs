using Microsoft.AspNetCore.Mvc;
using RabbitMqApi.Interfaces;
using RabbitMqApi.Models;
using RabbitMQ.Client.Exceptions;

namespace RabbitMqApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IRabbitMqPublisher _publisher;

        public OrdersController(IRabbitMqPublisher publisher)
        {
            _publisher = publisher;
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(
            [FromBody] OrderMessage order)
        {
            try
            {
                await _publisher.PublishAsync(order, "order.created");   // sipariş bilgisini alır işi yapmayı service e paslar

                return Ok(order);                       // isteğin http tipinde dönütü gelir
            }
            catch (BrokerUnreachableException)
            {
                return StatusCode(
                    503,
                    "RabbitMQ servisine şu anda ulaşılamıyor.");
            }
        }

        [HttpPost("notification")]
        public async Task<IActionResult> CreateOrderNotification(
            [FromBody] OrderMessage order)
        {
            try
            {
                await _publisher.PublishAsync(order, "order.notification");   // sipariş bilgisini alır işi yapmayı service e paslar

                return Ok(order);                       // isteğin http tipinde dönütü gelir
            }
            catch (BrokerUnreachableException)
            {
                return StatusCode(
                    503,
                    "RabbitMQ servisine şu anda ulaşılamıyor.");
            }
        }
    }
}
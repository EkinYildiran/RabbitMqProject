using Microsoft.AspNetCore.Mvc;
using RabbitMqApi.Interfaces;
using RabbitMqApi.Models;

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
            await _publisher.PublishAsync(order);   // sipariş bilgisini alır işi yapmayı service e paslar

            return Ok(order);                       // isteğin http tipinde dönütü gelir
        }
    }
}
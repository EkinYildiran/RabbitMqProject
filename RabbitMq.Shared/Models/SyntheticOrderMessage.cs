using System;

namespace RabbitMq.Shared.Models
{
    public class SyntheticOrderMessage
    {
        public Guid MessageId { get; set; }
        public int OrderId { get; set; }
        public int UserId { get; set; }
        public decimal Total { get; set; }
        public DateTime CreatedAt { get; set; }
        public string WorkerId { get; set; } = string.Empty;
    }
}
//SyntheticOrderMessage, Worker'larýn üreteceði sahte sipariþ verilerinin nasýl görüneceðini tanýmlayan C# sýnýfýdýr.
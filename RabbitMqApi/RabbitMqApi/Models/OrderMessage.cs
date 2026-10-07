namespace RabbitMqApi.Models
{
    public class OrderMessage
    {
        public int OrderId { get; set; }
        public int UserId { get; set; }
        public decimal Total { get; set; }

    }
}

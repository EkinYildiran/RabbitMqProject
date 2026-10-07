using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RabbitMqConsumer.Models
{
    public class OrderMessage
    {
        public int OrderId { get; set; }
        public int UserId { get; set; }
        public decimal Total { get; set; }
    }
}

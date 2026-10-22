using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseWorker.Entities
{
    public class OrderRecord
    {
        public int Id { get; set; } 
        public Guid MessageId { get; set; }   
        public int OrderId { get; set; }
        public int UserId { get; set;}
        public decimal Total {  get; set; }
        public DateTime CreatedAt {  get; set; }
        public string WorkerId { get; set; } = string.Empty;


    }
}

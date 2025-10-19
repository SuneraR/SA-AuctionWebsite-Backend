using System;

namespace SA_Project_API.Models
{
    public class Product
    {
        public int Id { get; set; }
        public int SellerId { get; set; }
        public User? Seller { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public decimal StartPrice { get; set; }
        public decimal CurrentPrice { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

<<<<<<< HEAD
=======
        public bool IsApproved { get; set; } = false;

>>>>>>> products
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}

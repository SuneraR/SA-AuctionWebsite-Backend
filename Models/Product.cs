using System;

namespace SA_Project_API.Models
{
    public class Product
    {
        public int Id { get; set; }
        public int SellerId { get; set; }
        public User? Seller { get; set; }

        public int? WinnerId { get; set; }
        public User? Winner { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public decimal StartPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal MinBidIncrement { get; set; } = 1.00m;

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public bool IsApproved { get; set; } = false;
        public string Status { get; set; } = "Draft"; // Draft, Pending, Active, Ended, Sold, Cancelled

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}

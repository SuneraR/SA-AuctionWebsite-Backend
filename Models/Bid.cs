using System;

namespace SA_Project_API.Models
{
    public class Bid
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public Product? Product { get; set; }

        public int BuyerId { get; set; }
        public User? Buyer { get; set; }

        public decimal BidAmount { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}

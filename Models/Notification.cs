using System;

namespace SA_Project_API.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }

        public string Type { get; set; } = string.Empty; // BidPlaced, BidOutbid, AuctionWon, PaymentDue, OrderPaid
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; }
    }
}

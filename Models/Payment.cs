using System;

namespace SA_Project_API.Models
{
    public class Payment
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order? Order { get; set; }

        public string PaymentMethod { get; set; } = "CreditCard"; // CreditCard, PayPal, BankTransfer
        public string? TransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Completed, Failed, Refunded

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}

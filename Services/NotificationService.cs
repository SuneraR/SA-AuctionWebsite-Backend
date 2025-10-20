using SA_Project_API.Data;
using SA_Project_API.Models;

namespace SA_Project_API.Services
{
    public interface INotificationService
    {
        Task SendNotificationAsync(int userId, string type, string message);
        Task NotifyBidPlacedAsync(int bidderId, int productId, decimal bidAmount);
        Task NotifyBidOutbidAsync(int previousBidderId, int productId);
        Task NotifyAuctionWonAsync(int winnerId, int productId);
        Task NotifyPaymentDueAsync(int buyerId, int orderId);
    }

    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(AppDbContext db, ILogger<NotificationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task SendNotificationAsync(int userId, string type, string message)
        {
            var notification = new Notification
            {
                UserId = userId,
                Type = type,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Notification sent to user {userId}: {type} - {message}");
        }

        public async Task NotifyBidPlacedAsync(int bidderId, int productId, decimal bidAmount)
        {
            var message = $"Your bid of ${bidAmount:F2} has been placed on product #{productId}.";
            await SendNotificationAsync(bidderId, "BidPlaced", message);
        }

        public async Task NotifyBidOutbidAsync(int previousBidderId, int productId)
        {
            var message = $"You have been outbid on product #{productId}. Place a higher bid to win!";
            await SendNotificationAsync(previousBidderId, "BidOutbid", message);
        }

        public async Task NotifyAuctionWonAsync(int winnerId, int productId)
        {
            var message = $"Congratulations! You won the auction for product #{productId}. Please complete payment.";
            await SendNotificationAsync(winnerId, "AuctionWon", message);
        }

        public async Task NotifyPaymentDueAsync(int buyerId, int orderId)
        {
            var message = $"Payment is due for order #{orderId}. Please complete your purchase.";
            await SendNotificationAsync(buyerId, "PaymentDue", message);
        }
    }
}

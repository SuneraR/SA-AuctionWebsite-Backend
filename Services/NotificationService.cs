using SA_Project_API.Data;
using SA_Project_API.Models;
using Microsoft.EntityFrameworkCore;

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
        private readonly IEmailService _emailService;

        public NotificationService(AppDbContext db, ILogger<NotificationService> logger, IEmailService emailService)
        {
            _db = db;
            _logger = logger;
            _emailService = emailService;
        }

        public async Task SendNotificationAsync(int userId, string type, string message)
        {
            // Create in-app notification
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

            // Send email notification
            var user = await _db.Users.FindAsync(bidderId);
            var product = await _db.Products.FindAsync(productId);

            if (user != null && product != null)
            {
                await _emailService.SendBidPlacedEmailAsync(
                    user.Email,
                    user.FirstName,
                    product.Name,
                    bidAmount
                );
            }
        }

        public async Task NotifyBidOutbidAsync(int previousBidderId, int productId)
        {
            var message = $"You have been outbid on product #{productId}. Place a higher bid to win!";
            await SendNotificationAsync(previousBidderId, "BidOutbid", message);

            // Send email notification
            var user = await _db.Users.FindAsync(previousBidderId);
            var product = await _db.Products.FindAsync(productId);

            if (user != null && product != null)
            {
                await _emailService.SendBidOutbidEmailAsync(
                    user.Email,
                    user.FirstName,
                    product.Name
                );
            }
        }

        public async Task NotifyAuctionWonAsync(int winnerId, int productId)
        {
            var product = await _db.Products.FindAsync(productId);
            var message = $"Congratulations! You won the auction for product #{productId}. Please complete payment.";
            await SendNotificationAsync(winnerId, "AuctionWon", message);

            // Send email notification
            var user = await _db.Users.FindAsync(winnerId);

            if (user != null && product != null)
            {
                await _emailService.SendAuctionWonEmailAsync(
                    user.Email,
                    user.FirstName,
                    product.Name,
                    product.CurrentPrice
                );
            }
        }

        public async Task NotifyPaymentDueAsync(int buyerId, int orderId)
        {
            var order = await _db.Orders.Include(o => o.Product).FirstOrDefaultAsync(o => o.Id == orderId);
            var message = $"Payment is due for order #{orderId}. Please complete your purchase.";
            await SendNotificationAsync(buyerId, "PaymentDue", message);

            // Send email notification
            var user = await _db.Users.FindAsync(buyerId);

            if (user != null && order != null)
            {
                await _emailService.SendPaymentReminderEmailAsync(
                    user.Email,
                    user.FirstName,
                    orderId,
                    order.FinalPrice
                );
            }
        }
    }
}

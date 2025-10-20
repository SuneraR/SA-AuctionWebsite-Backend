using Microsoft.EntityFrameworkCore;
using SA_Project_API.Data;
using SA_Project_API.Models;
using SA_Project_API.Services;

namespace SA_Project_API.Services
{
    public class AuctionBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AuctionBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1); // Check every minute

        public AuctionBackgroundService(IServiceProvider serviceProvider, ILogger<AuctionBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Auction Background Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessEndedAuctionsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing ended auctions.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Auction Background Service stopped.");
        }

        private async Task ProcessEndedAuctionsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;

            // Find auctions that have ended but not yet processed
            var endedAuctions = await db.Products
                .Where(p => p.EndTime <= now && p.Status == "Active" && p.IsApproved)
                .ToListAsync();

            _logger.LogInformation($"Found {endedAuctions.Count} ended auctions to process.");

            foreach (var product in endedAuctions)
            {
                try
                {
                    await ProcessSingleAuctionAsync(db, notificationService, product);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing auction for product {product.Id}");
                }
            }

            if (endedAuctions.Any())
            {
                await db.SaveChangesAsync();
            }
        }

        private async Task ProcessSingleAuctionAsync(AppDbContext db, INotificationService notificationService, Product product)
        {
            // Find the highest bid
            var highestBid = await db.Bids
                .Where(b => b.ProductId == product.Id)
                .OrderByDescending(b => b.BidAmount)
                .ThenByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            if (highestBid == null)
            {
                // No bids - mark as ended with no winner
                product.Status = "Ended";
                product.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation($"Product {product.Id} ended with no bids.");
                return;
            }

            // Set the winner
            product.WinnerId = highestBid.BuyerId;
            product.Status = "Ended";
            product.UpdatedAt = DateTime.UtcNow;

            // Check if order already exists
            var existingOrder = await db.Orders.AnyAsync(o => o.ProductId == product.Id);
            if (!existingOrder)
            {
                // Create order for the winner
                var order = new Order
                {
                    ProductId = product.Id,
                    BuyerId = highestBid.BuyerId,
                    FinalPrice = highestBid.BidAmount,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                };

                db.Orders.Add(order);

                // Notify winner
                await notificationService.NotifyAuctionWonAsync(highestBid.BuyerId, product.Id);
                await notificationService.NotifyPaymentDueAsync(highestBid.BuyerId, order.Id);

                _logger.LogInformation($"Order created for product {product.Id}, winner: {highestBid.BuyerId}");
            }

            db.Products.Update(product);
        }
    }
}

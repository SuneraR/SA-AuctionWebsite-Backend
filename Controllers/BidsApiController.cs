using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SA_Project_API.Data;
using SA_Project_API.Models;
using SA_Project_API.Services;

namespace SA_Project_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BidsApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<BidsApiController> _logger;
        private readonly INotificationService _notificationService;

        public BidsApiController(AppDbContext db, ILogger<BidsApiController> logger, INotificationService notificationService)
        {
            _db = db;
            _logger = logger;
            _notificationService = notificationService;
        }

        // POST: api/Bids
        [HttpPost]
        public async Task<IActionResult> PlaceBid(PlaceBidRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request");

            // Verify buyer exists
            var buyerExists = await _db.Users.AnyAsync(u => u.Id == request.BuyerId);
            if (!buyerExists)
                return BadRequest("Buyer does not exist.");

            // Get product and verify it exists
            var product = await _db.Products.FindAsync(request.ProductId);
            if (product == null)
                return NotFound("Product not found.");

            // Prevent self-bidding
            if (product.SellerId == request.BuyerId)
                return BadRequest("Sellers cannot bid on their own products.");

            // Check if auction is active and approved
            if (!product.IsApproved)
                return BadRequest("This product is not approved for bidding.");

            if (product.Status != "Active")
                return BadRequest($"Auction is not active. Current status: {product.Status}");

            // Check if auction is still active (time-based)
            var now = DateTime.UtcNow;
            if (now < product.StartTime)
                return BadRequest("Auction has not started yet.");

            if (now > product.EndTime)
                return BadRequest("Auction has ended.");

            // Verify bid amount is higher than current price + minimum increment
            var minimumBid = product.CurrentPrice + product.MinBidIncrement;
            if (request.BidAmount < minimumBid)
                return BadRequest($"Bid amount must be at least ${minimumBid:F2} (current price + minimum increment of ${product.MinBidIncrement:F2}).");

            // Get the previous highest bidder to notify them
            var previousHighestBid = await _db.Bids
                .Where(b => b.ProductId == request.ProductId)
                .OrderByDescending(b => b.BidAmount)
                .FirstOrDefaultAsync();

            // Create bid
            var bid = new Bid
            {
                ProductId = request.ProductId,
                BuyerId = request.BuyerId,
                BidAmount = request.BidAmount,
                CreatedAt = DateTime.UtcNow
            };

            _db.Bids.Add(bid);

            // Update product's current price
            product.CurrentPrice = request.BidAmount;
            product.UpdatedAt = DateTime.UtcNow;
            _db.Products.Update(product);

            await _db.SaveChangesAsync();

            // Send notifications
            await _notificationService.NotifyBidPlacedAsync(request.BuyerId, request.ProductId, request.BidAmount);

            // Notify previous highest bidder they've been outbid
            if (previousHighestBid != null && previousHighestBid.BuyerId != request.BuyerId)
            {
                await _notificationService.NotifyBidOutbidAsync(previousHighestBid.BuyerId, request.ProductId);
            }

            return CreatedAtAction(nameof(GetById), new { id = bid.Id }, bid);
        }

        // GET: api/Bids/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var bid = await _db.Bids
                .Include(b => b.Buyer)
                .Include(b => b.Product)
                .SingleOrDefaultAsync(b => b.Id == id);

            if (bid == null)
                return NotFound();

            return Ok(bid);
        }

        // GET: api/Bids/product/{productId}
        [HttpGet("product/{productId:int}")]
        public async Task<IActionResult> GetBidsByProduct(int productId, [FromQuery] int limit = 50)
        {
            var bids = await _db.Bids
                .Where(b => b.ProductId == productId)
                .Include(b => b.Buyer)
                .OrderByDescending(b => b.BidAmount)
                .ThenByDescending(b => b.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(bids);
        }

        // GET: api/Bids/buyer/{buyerId}
        [HttpGet("buyer/{buyerId:int}")]
        public async Task<IActionResult> GetBidsByBuyer(int buyerId, [FromQuery] int limit = 50)
        {
            var bids = await _db.Bids
                .Where(b => b.BuyerId == buyerId)
                .Include(b => b.Product)
                .OrderByDescending(b => b.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(bids);
        }

        // GET: api/Bids/product/{productId}/highest
        [HttpGet("product/{productId:int}/highest")]
        public async Task<IActionResult> GetHighestBid(int productId)
        {
            var highestBid = await _db.Bids
                .Where(b => b.ProductId == productId)
                .Include(b => b.Buyer)
                .OrderByDescending(b => b.BidAmount)
                .ThenByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            if (highestBid == null)
                return NotFound("No bids found for this product.");

            return Ok(highestBid);
        }

        // DTOs
        public record PlaceBidRequest(int ProductId, int BuyerId, decimal BidAmount);
    }
}

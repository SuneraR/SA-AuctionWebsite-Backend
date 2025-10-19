using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SA_Project_API.Data;
using SA_Project_API.Models;

namespace SA_Project_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BidsApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<BidsApiController> _logger;

        public BidsApiController(AppDbContext db, ILogger<BidsApiController> logger)
        {
            _db = db;
            _logger = logger;
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

            // Check if auction is still active
            var now = DateTime.UtcNow;
            if (now < product.StartTime)
                return BadRequest("Auction has not started yet.");

            if (now > product.EndTime)
                return BadRequest("Auction has ended.");

            // Verify bid amount is higher than current price
            if (request.BidAmount <= product.CurrentPrice)
                return BadRequest($"Bid amount must be higher than current price ({product.CurrentPrice}).");

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

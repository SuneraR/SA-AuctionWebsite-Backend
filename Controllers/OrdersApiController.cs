using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SA_Project_API.Data;
using SA_Project_API.Models;
using System.Security.Claims;

namespace SA_Project_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<OrdersApiController> _logger;

        public OrdersApiController(AppDbContext db, ILogger<OrdersApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // POST: api/Orders
        // Create order after auction ends - typically for the highest bidder
        [HttpPost]
        public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Verify product exists
            var product = await _db.Products.FindAsync(request.ProductId);
            if (product == null)
                return NotFound("Product not found.");

            // Check if auction has ended
            if (DateTime.UtcNow < product.EndTime)
                return BadRequest("Auction has not ended yet. Cannot create order.");

            // Use authenticated user as buyer unless Admin
            var buyerId = request.BuyerId ?? userId;
            if (buyerId != userId && !User.IsInRole("Admin"))
                return Forbid("You can only create orders for yourself.");

            // Verify buyer exists
            var buyerExists = await _db.Users.AnyAsync(u => u.Id == buyerId);
            if (!buyerExists)
                return BadRequest("Buyer does not exist.");

            // Check if order already exists for this product
            var existingOrder = await _db.Orders.AnyAsync(o => o.ProductId == request.ProductId);
            if (existingOrder)
                return Conflict("Order already exists for this product.");

            // Verify buyer has the highest bid
            var highestBid = await _db.Bids
                .Where(b => b.ProductId == request.ProductId)
                .OrderByDescending(b => b.BidAmount)
                .ThenByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            if (highestBid == null)
                return BadRequest("No bids found for this product.");

            if (highestBid.BuyerId != buyerId && !User.IsInRole("Admin"))
                return BadRequest("Only the highest bidder can create an order for this product.");

            var order = new Order
            {
                ProductId = request.ProductId,
                BuyerId = buyerId,
                FinalPrice = highestBid.BidAmount,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }

        // GET: api/Orders/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var order = await _db.Orders
                .Include(o => o.Product)
                    .ThenInclude(p => p!.Seller)
                .Include(o => o.Buyer)
                .SingleOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound();

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Only buyer, seller, or admin can view order
            if (order.BuyerId != userId && order.Product?.SellerId != userId && !User.IsInRole("Admin"))
                return Forbid("You do not have permission to view this order.");

            return Ok(order);
        }

        // GET: api/Orders/my-purchases
        [HttpGet("my-purchases")]
        public async Task<IActionResult> GetMyPurchases([FromQuery] int limit = 50)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var orders = await _db.Orders
                .Where(o => o.BuyerId == userId)
                .Include(o => o.Product)
                .OrderByDescending(o => o.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(orders);
        }

        // GET: api/Orders/my-sales
        [HttpGet("my-sales")]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> GetMySales([FromQuery] int limit = 50)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var orders = await _db.Orders
                .Include(o => o.Product)
                .Include(o => o.Buyer)
                .Where(o => o.Product!.SellerId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(orders);
        }

        // GET: api/Orders/buyer/{buyerId}
        [HttpGet("buyer/{buyerId:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetOrdersByBuyer(int buyerId, [FromQuery] int limit = 50)
        {
            var orders = await _db.Orders
                .Where(o => o.BuyerId == buyerId)
                .Include(o => o.Product)
                .OrderByDescending(o => o.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(orders);
        }

        // GET: api/Orders/seller/{sellerId}
        [HttpGet("seller/{sellerId:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetOrdersBySeller(int sellerId, [FromQuery] int limit = 50)
        {
            var orders = await _db.Orders
                .Include(o => o.Product)
                .Include(o => o.Buyer)
                .Where(o => o.Product!.SellerId == sellerId)
                .OrderByDescending(o => o.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(orders);
        }

        // PUT: api/Orders/{id}/status
        [HttpPut("{id:int}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, UpdateOrderStatusRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Status))
                return BadRequest("Status is required.");

            var validStatuses = new[] { "Pending", "Paid", "Cancelled" };
            if (!validStatuses.Contains(request.Status))
                return BadRequest($"Invalid status. Valid values: {string.Join(", ", validStatuses)}");

            var order = await _db.Orders
                .Include(o => o.Product)
                .SingleOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound();

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Only buyer, seller, or admin can update status
            if (order.BuyerId != userId && order.Product?.SellerId != userId && !User.IsInRole("Admin"))
                return Forbid("You do not have permission to update this order.");

            // Prevent updating to same status
            if (order.Status == request.Status)
                return BadRequest($"Order is already in '{request.Status}' status.");

            // Business logic: Cancelled orders cannot be changed
            if (order.Status == "Cancelled")
                return BadRequest("Cannot update a cancelled order.");

            // Business logic: Paid orders can only be cancelled (by seller or admin)
            if (order.Status == "Paid" && request.Status != "Cancelled")
                return BadRequest("Paid orders can only be cancelled.");

            order.Status = request.Status;

            // If order is paid, mark product as Sold
            if (request.Status == "Paid" && order.Product != null)
            {
                order.Product.Status = "Sold";
                order.Product.UpdatedAt = DateTime.UtcNow;
                _db.Products.Update(order.Product);
            }

            _db.Orders.Update(order);
            await _db.SaveChangesAsync();

            return Ok(order);
        }

        // DELETE: api/Orders/{id}
        // Only allow deletion if status is Pending
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            var order = await _db.Orders.FindAsync(id);
            if (order == null)
                return NotFound();

            if (order.Status != "Pending")
                return BadRequest("Only pending orders can be deleted. Use cancel instead.");

            _db.Orders.Remove(order);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // POST: api/Orders/create-for-winner/{productId}
        // Automatically create order for the highest bidder after auction ends
        [HttpPost("create-for-winner/{productId:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateOrderForWinner(int productId)
        {
            var product = await _db.Products.FindAsync(productId);
            if (product == null)
                return NotFound("Product not found.");

            if (DateTime.UtcNow < product.EndTime)
                return BadRequest("Auction has not ended yet.");

            // Check if order already exists
            var existingOrder = await _db.Orders.AnyAsync(o => o.ProductId == productId);
            if (existingOrder)
                return Conflict("Order already exists for this product.");

            // Find highest bidder
            var highestBid = await _db.Bids
                .Where(b => b.ProductId == productId)
                .OrderByDescending(b => b.BidAmount)
                .ThenByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            if (highestBid == null)
                return BadRequest("No bids found for this product. Cannot create order.");

            var order = new Order
            {
                ProductId = productId,
                BuyerId = highestBid.BuyerId,
                FinalPrice = highestBid.BidAmount,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }

        // DTOs
        public record CreateOrderRequest(int ProductId, int? BuyerId);
        public record UpdateOrderStatusRequest(string Status);
    }
}

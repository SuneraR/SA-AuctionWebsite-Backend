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
    public class ProductsApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ProductsApiController> _logger;

        public ProductsApiController(AppDbContext db, ILogger<ProductsApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // POST: api/Products
        [HttpPost]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> Create(CreateProductRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request");

            if (request.StartTime >= request.EndTime)
                return BadRequest("StartTime must be earlier than EndTime.");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Use authenticated user as seller
            var sellerId = request.SellerId ?? userId;

            // Verify seller exists
            var sellerExists = await _db.Users.AnyAsync(u => u.Id == sellerId);
            if (!sellerExists)
                return BadRequest("Seller does not exist.");

            // Only allow creating product for self unless Admin
            if (sellerId != userId && !User.IsInRole("Admin"))
                return Forbid("You can only create products for yourself.");

            var product = new Product
            {
                SellerId = sellerId,
                Name = request.Name,
                Description = request.Description,
                StartPrice = request.StartPrice,
                CurrentPrice = request.StartPrice,
                MinBidIncrement = request.MinBidIncrement ?? 1.00m,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                IsApproved = User.IsInRole("Admin"), // Auto-approve for admins
                Status = "Pending", // Pending approval
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        // GET: api/Products/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _db.Products
                .Include(p => p.Seller)
                .Include(p => p.Winner)
                .SingleOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound();

            return Ok(product);
        }

        // GET: api/Products/search?name=abc
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string? name, [FromQuery] string? status, [FromQuery] int limit = 50)
        {
            var query = _db.Products.AsQueryable();

            if (!string.IsNullOrWhiteSpace(name))
            {
                var q = name.Trim();
                query = query.Where(p => EF.Functions.Like(p.Name, $"%{q}%"));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status == status);
            }

            // Only show approved products for public search
            query = query.Where(p => p.IsApproved);

            var results = await query
                .OrderBy(p => p.StartTime)
                .Take(limit)
                .ToListAsync();

            return Ok(results);
        }

        // GET: api/Products/active
        [HttpGet("active")]
        public async Task<IActionResult> GetActive([FromQuery] int limit = 50)
        {
            var now = DateTime.UtcNow;
            var activeProducts = await _db.Products
                .Where(p => p.IsApproved && p.Status == "Active" && p.StartTime <= now && p.EndTime > now)
                .OrderBy(p => p.EndTime)
                .Take(limit)
                .ToListAsync();

            return Ok(activeProducts);
        }

        // PUT: api/Products/{id}
        [HttpPut("{id:int}")]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> Update(int id, UpdateProductRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request");

            var product = await _db.Products.FindAsync(id);
            if (product == null)
                return NotFound();

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Only seller or admin can update
            if (product.SellerId != userId && !User.IsInRole("Admin"))
                return Forbid("You can only update your own products.");

            // Prevent updating products with bids (unless admin)
            if (!User.IsInRole("Admin"))
            {
                var hasBids = await _db.Bids.AnyAsync(b => b.ProductId == id);
                if (hasBids)
                    return BadRequest("Cannot update product that has bids.");
            }

            var newStart = request.StartTime ?? product.StartTime;
            var newEnd = request.EndTime ?? product.EndTime;
            if (newStart >= newEnd)
                return BadRequest("StartTime must be earlier than EndTime.");

            product.Name = request.Name ?? product.Name;
            product.Description = request.Description ?? product.Description;
            product.StartPrice = request.StartPrice ?? product.StartPrice;
            product.MinBidIncrement = request.MinBidIncrement ?? product.MinBidIncrement;
            product.StartTime = newStart;
            product.EndTime = newEnd;
            product.UpdatedAt = DateTime.UtcNow;

            _db.Products.Update(product);
            await _db.SaveChangesAsync();

            return Ok(product);
        }

        // PUT: api/Products/{id}/approve
        [HttpPut("{id:int}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveProduct(int id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null)
                return NotFound();

            if (product.IsApproved)
                return BadRequest("Product is already approved.");

            product.IsApproved = true;
            product.Status = "Active";
            product.UpdatedAt = DateTime.UtcNow;

            _db.Products.Update(product);
            await _db.SaveChangesAsync();

            return Ok(product);
        }

        // DELETE: api/Products/{id}
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null)
                return NotFound();

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Only seller or admin can delete
            if (product.SellerId != userId && !User.IsInRole("Admin"))
                return Forbid("You can only delete your own products.");

            // Prevent deleting products with bids (unless admin)
            if (!User.IsInRole("Admin"))
            {
                var hasBids = await _db.Bids.AnyAsync(b => b.ProductId == id);
                if (hasBids)
                    return BadRequest("Cannot delete product that has bids. Cancel the auction instead.");
            }

            _db.Products.Remove(product);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // DTOs
        public record CreateProductRequest(int? SellerId, string Name, string? Description, decimal StartPrice, DateTime StartTime, DateTime EndTime, decimal? MinBidIncrement);
        public record UpdateProductRequest(string? Name, string? Description, decimal? StartPrice, decimal? MinBidIncrement, DateTime? StartTime, DateTime? EndTime);
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SA_Project_API.Data;
using SA_Project_API.Models;

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
        public async Task<IActionResult> Create(CreateProductRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request");

            if (request.StartTime >= request.EndTime)
                return BadRequest("StartTime must be earlier than EndTime.");

            // ensure seller exists
            var sellerExists = await _db.Users.AnyAsync(u => u.Id == request.SellerId);
            if (!sellerExists)
                return BadRequest("Seller does not exist.");

            var product = new Product
            {
                SellerId = request.SellerId,
                Name = request.Name,
                Description = request.Description,
                StartPrice = request.StartPrice,
                CurrentPrice = request.StartPrice,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
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
            var product = await _db.Products.Include(p => p.Seller).SingleOrDefaultAsync(p => p.Id == id);
            if (product == null)
                return NotFound();

            return Ok(product);
        }

        // GET: api/Products/search?name=abc
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string name, [FromQuery] int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Query 'name' is required.");

            var q = name.Trim();
            var results = await _db.Products
                .Where(p => EF.Functions.Like(p.Name, $"%{q}%"))
                .OrderBy(p => p.StartTime)
                .Take(limit)
                .ToListAsync();

            return Ok(results);
        }

        // PUT: api/Products/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, UpdateProductRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request");

            var product = await _db.Products.FindAsync(id);
            if (product == null)
                return NotFound();

            if (request.StartTime >= request.EndTime)
                return BadRequest("StartTime must be earlier than EndTime.");

            // Only allow updating specific fields
            product.Name = request.Name ?? product.Name;
            product.Description = request.Description ?? product.Description;
            product.StartPrice = request.StartPrice ?? product.StartPrice;
            product.CurrentPrice = request.CurrentPrice ?? product.CurrentPrice;
            product.StartTime = request.StartTime ?? product.StartTime;
            product.EndTime = request.EndTime ?? product.EndTime;
            product.UpdatedAt = DateTime.UtcNow;

            _db.Products.Update(product);
            await _db.SaveChangesAsync();

            return Ok(product);
        }

        // DELETE: api/Products/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null)
                return NotFound();

            _db.Products.Remove(product);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // DTOs
        public record CreateProductRequest(int SellerId, string Name, string? Description, decimal StartPrice, DateTime StartTime, DateTime EndTime);

        public record UpdateProductRequest(string? Name, string? Description, decimal? StartPrice, decimal? CurrentPrice, DateTime? StartTime, DateTime? EndTime);
    }
}

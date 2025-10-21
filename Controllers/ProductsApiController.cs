using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SA_Project_API.Data;
using SA_Project_API.Models;
using SA_Project_API.Services;
using System.Security.Claims;

namespace SA_Project_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ProductsApiController> _logger;
        private readonly IImageUploadService _imageUploadService;

        public ProductsApiController(AppDbContext db, ILogger<ProductsApiController> logger, IImageUploadService imageUploadService)
        {
            _db = db;
            _logger = logger;
            _imageUploadService = imageUploadService;
        }

        // POST: api/Products
        [HttpPost]
        [Authorize(Roles = "Seller,Admin")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Create([FromForm] CreateProductRequest request)
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
                Name = request.Name ?? string.Empty,
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

            // Handle image uploads
            if (request.Images != null && request.Images.Count > 0)
            {
                var imageUrls = new List<string>();
                foreach (var imageFile in request.Images)
                {
                    if (_imageUploadService.IsValidImage(imageFile))
                    {
                        try
                        {
                            var imageUrl = await _imageUploadService.SaveImageAsync(imageFile);
                            imageUrls.Add(imageUrl);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to upload image: {imageFile.FileName}");
                        }
                    }
                }

                // Create ProductImage records
                for (int i = 0; i < imageUrls.Count; i++)
                {
                    var productImage = new ProductImage
                    {
                        ProductId = product.Id,
                        ImageUrl = imageUrls[i],
                        IsPrimary = i == 0, // First image is primary
                        DisplayOrder = i,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.ProductImages.Add(productImage);
                }

                await _db.SaveChangesAsync();
            }

            // Load images for response
            var createdProduct = await _db.Products
                .Include(p => p.Images)
                .Include(p => p.Seller)
                .FirstOrDefaultAsync(p => p.Id == product.Id);

            return CreatedAtAction(nameof(GetById), new { id = product.Id }, createdProduct);
        }

        // POST: api/Products/{id}/images
        [HttpPost("{id:int}/images")]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> AddImages(int id, [FromForm] List<IFormFile> images)
        {
            var product = await _db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
            if (product == null)
                return NotFound();

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Only seller or admin can add images
            if (product.SellerId != userId && !User.IsInRole("Admin"))
                return Forbid("You can only add images to your own products.");

            if (images == null || images.Count == 0)
                return BadRequest("No images provided");

            var addedImages = new List<ProductImage>();
            var currentMaxOrder = product.Images.Any() ? product.Images.Max(i => i.DisplayOrder) : -1;

            foreach (var imageFile in images)
            {
                if (_imageUploadService.IsValidImage(imageFile))
                {
                    try
                    {
                        var imageUrl = await _imageUploadService.SaveImageAsync(imageFile);
                        currentMaxOrder++;

                        var productImage = new ProductImage
                        {
                            ProductId = id,
                            ImageUrl = imageUrl,
                            IsPrimary = !product.Images.Any() && addedImages.Count == 0, // First image is primary if no images exist
                            DisplayOrder = currentMaxOrder,
                            CreatedAt = DateTime.UtcNow
                        };

                        _db.ProductImages.Add(productImage);
                        addedImages.Add(productImage);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to upload image: {imageFile.FileName}");
                    }
                }
            }

            await _db.SaveChangesAsync();

            return Ok(addedImages);
        }

        // DELETE: api/Products/{productId}/images/{imageId}
        [HttpDelete("{productId:int}/images/{imageId:int}")]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> DeleteImage(int productId, int imageId)
        {
            var product = await _db.Products.FindAsync(productId);
            if (product == null)
                return NotFound("Product not found");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Only seller or admin can delete images
            if (product.SellerId != userId && !User.IsInRole("Admin"))
                return Forbid("You can only delete images from your own products.");

            var image = await _db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId);
            if (image == null)
                return NotFound("Image not found");

            // Delete file from disk
            await _imageUploadService.DeleteImageAsync(image.ImageUrl);

            // Remove from database
            _db.ProductImages.Remove(image);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // PUT: api/Products/{productId}/images/{imageId}/set-primary
        [HttpPut("{productId:int}/images/{imageId:int}/set-primary")]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> SetPrimaryImage(int productId, int imageId)
        {
            var product = await _db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == productId);
            if (product == null)
                return NotFound("Product not found");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Only seller or admin can set primary image
            if (product.SellerId != userId && !User.IsInRole("Admin"))
                return Forbid("You can only modify your own products.");

            var image = product.Images.FirstOrDefault(i => i.Id == imageId);
            if (image == null)
                return NotFound("Image not found");

            // Remove primary flag from all images
            foreach (var img in product.Images)
            {
                img.IsPrimary = false;
            }

            // Set new primary
            image.IsPrimary = true;

            _db.ProductImages.UpdateRange(product.Images);
            await _db.SaveChangesAsync();

            return Ok(image);
        }

        // GET: api/Products/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _db.Products
                .Include(p => p.Seller)
                .Include(p => p.Winner)
                .Include(p => p.Images.OrderBy(i => i.DisplayOrder))
                .SingleOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound();

            return Ok(product);
        }

        // GET: api/Products/search?name=abc
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string? name, [FromQuery] string? status, [FromQuery] int limit = 50)
        {
            var query = _db.Products
                .Include(p => p.Images.Where(i => i.IsPrimary))
                .AsQueryable();

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
                .Include(p => p.Images.Where(i => i.IsPrimary))
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
            var product = await _db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
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

            // Delete all product images from disk
            foreach (var image in product.Images)
            {
                await _imageUploadService.DeleteImageAsync(image.ImageUrl);
            }

            _db.Products.Remove(product);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // DTOs
        public class CreateProductRequest
        {
            public int? SellerId { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public decimal StartPrice { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public decimal? MinBidIncrement { get; set; }
            public List<IFormFile>? Images { get; set; }
        }

        public record UpdateProductRequest(string? Name, string? Description, decimal? StartPrice, decimal? MinBidIncrement, DateTime? StartTime, DateTime? EndTime);
    }
}

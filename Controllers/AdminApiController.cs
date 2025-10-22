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
    [Authorize(Roles = "Admin")]
    public class AdminApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AdminApiController> _logger;
        private readonly IImageUploadService _imageUploadService;
        private readonly INotificationService _notificationService;

        public AdminApiController(
            AppDbContext db, 
            ILogger<AdminApiController> logger,
            IImageUploadService imageUploadService,
            INotificationService notificationService)
        {
            _db = db;
            _logger = logger;
            _imageUploadService = imageUploadService;
            _notificationService = notificationService;
        }

        #region Product Management

        // GET: api/Admin/products/pending
        [HttpGet("products/pending")]
        public async Task<IActionResult> GetPendingProducts([FromQuery] int page = 1, [FromQuery] int limit = 20)
        {
            var skip = (page - 1) * limit;
            
            var totalCount = await _db.Products
                .Where(p => !p.IsApproved && p.Status == "Pending")
                .CountAsync();

            var products = await _db.Products
                .Include(p => p.Seller)
                .Include(p => p.Images.Where(i => i.IsPrimary))
                .Where(p => !p.IsApproved && p.Status == "Pending")
                .OrderBy(p => p.CreatedAt)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();

            return Ok(new
            {
                products,
                totalCount,
                page,
                totalPages = (int)Math.Ceiling(totalCount / (double)limit)
            });
        }

        // GET: api/Admin/products
        [HttpGet("products")]
        public async Task<IActionResult> GetAllProducts(
            [FromQuery] string? status,
            [FromQuery] bool? isApproved,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20)
        {
            var skip = (page - 1) * limit;
            var query = _db.Products.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status == status);
            }

            if (isApproved.HasValue)
            {
                query = query.Where(p => p.IsApproved == isApproved.Value);
            }

            var totalCount = await query.CountAsync();

            var products = await query
                .Include(p => p.Seller)
                .Include(p => p.Images.Where(i => i.IsPrimary))
                .OrderByDescending(p => p.CreatedAt)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();

            return Ok(new
            {
                products,
                totalCount,
                page,
                totalPages = (int)Math.Ceiling(totalCount / (double)limit)
            });
        }

        // PUT: api/Admin/products/{id}/approve
        [HttpPut("products/{id:int}/approve")]
        public async Task<IActionResult> ApproveProduct(int id, [FromBody] ApproveProductRequest? request)
        {
            var product = await _db.Products
                .Include(p => p.Seller)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound("Product not found.");

            if (product.IsApproved)
                return BadRequest("Product is already approved.");

            product.IsApproved = true;
            product.Status = "Active";
            product.UpdatedAt = DateTime.UtcNow;

            _db.Products.Update(product);
            await _db.SaveChangesAsync();

            // Send notification to seller
            await _notificationService.CreateNotificationAsync(
                product.SellerId,
                "Product Approved",
                $"Your product '{product.Name}' has been approved and is now live!",
                "ProductApproved",
                product.Id
            );

            _logger.LogInformation($"Product {id} approved by admin");

            return Ok(new
            {
                message = "Product approved successfully",
                product
            });
        }

        // PUT: api/Admin/products/{id}/reject
        [HttpPut("products/{id:int}/reject")]
        public async Task<IActionResult> RejectProduct(int id, [FromBody] RejectProductRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest("Rejection reason is required.");

            var product = await _db.Products
                .Include(p => p.Seller)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound("Product not found.");

            if (product.IsApproved)
                return BadRequest("Cannot reject an already approved product.");

            // Send notification to seller
            await _notificationService.CreateNotificationAsync(
                product.SellerId,
                "Product Rejected",
                $"Your product '{product.Name}' has been rejected. Reason: {request.Reason}",
                "ProductRejected",
                product.Id
            );

            // Delete product images from disk
            foreach (var image in product.Images)
            {
                await _imageUploadService.DeleteImageAsync(image.ImageUrl);
            }

            // Delete the product
            _db.Products.Remove(product);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Product {id} rejected by admin. Reason: {request.Reason}");

            return Ok(new
            {
                message = "Product rejected and removed successfully",
                reason = request.Reason
            });
        }

        // DELETE: api/Admin/products/{id}/force-delete
        [HttpDelete("products/{id:int}/force-delete")]
        public async Task<IActionResult> ForceDeleteProduct(int id, [FromBody] DeleteProductRequest? request)
        {
            var product = await _db.Products
                .Include(p => p.Images)
                .Include(p => p.Seller)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound("Product not found.");

            var hasBids = await _db.Bids.AnyAsync(b => b.ProductId == id);
            var reason = request?.Reason ?? "Administrative action";

            // Send notification to seller
            await _notificationService.CreateNotificationAsync(
                product.SellerId,
                "Product Deleted by Admin",
                $"Your product '{product.Name}' has been removed by an administrator. Reason: {reason}",
                "ProductDeleted",
                product.Id
            );

            // If product has bids, notify all bidders
            if (hasBids)
            {
                var bidders = await _db.Bids
                    .Where(b => b.ProductId == id)
                    .Select(b => b.BuyerId)
                    .Distinct()
                    .ToListAsync();

                foreach (var bidderId in bidders)
                {
                    await _notificationService.CreateNotificationAsync(
                        bidderId,
                        "Auction Cancelled",
                        $"The auction for '{product.Name}' has been cancelled by an administrator.",
                        "AuctionCancelled",
                        product.Id
                    );
                }
            }

            // Delete product images from disk
            foreach (var image in product.Images)
            {
                await _imageUploadService.DeleteImageAsync(image.ImageUrl);
            }

            // Delete the product (cascading will handle bids, images, etc.)
            _db.Products.Remove(product);
            await _db.SaveChangesAsync();

            _logger.LogWarning($"Product {id} force-deleted by admin. Reason: {reason}");

            return Ok(new
            {
                message = "Product deleted successfully",
                hadBids = hasBids,
                reason
            });
        }

        #endregion

        #region User Management

        // GET: api/Admin/users
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers(
            [FromQuery] string? role,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20)
        {
            var skip = (page - 1) * limit;
            var query = _db.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(role))
            {
                query = query.Where(u => u.Role == role);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTerm = search.Trim();
                query = query.Where(u =>
                    u.Email.Contains(searchTerm) ||
                    u.FirstName.Contains(searchTerm) ||
                    u.LastName.Contains(searchTerm));
            }

            var totalCount = await query.CountAsync();

            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip(skip)
                .Take(limit)
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.Role,
                    u.CreatedAt,
                    u.UpdatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                users,
                totalCount,
                page,
                totalPages = (int)Math.Ceiling(totalCount / (double)limit)
            });
        }

        // GET: api/Admin/users/{id}
        [HttpGet("users/{id:int}")]
        public async Task<IActionResult> GetUserDetails(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound("User not found.");

            var productCount = await _db.Products.CountAsync(p => p.SellerId == id);
            var bidCount = await _db.Bids.CountAsync(b => b.BuyerId == id);
            var wonAuctions = await _db.Products.CountAsync(p => p.WinnerId == id && p.Status == "Sold");

            return Ok(new
            {
                user = new
                {
                    user.Id,
                    user.FirstName,
                    user.LastName,
                    user.Email,
                    user.Role,
                    user.CreatedAt,
                    user.UpdatedAt
                },
                stats = new
                {
                    productsListed = productCount,
                    bidsPlaced = bidCount,
                    auctionsWon = wonAuctions
                }
            });
        }

        // PUT: api/Admin/users/{id}/role
        [HttpPut("users/{id:int}/role")]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] UpdateRoleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Role))
                return BadRequest("Role is required.");

            var validRoles = new[] { "Admin", "Seller", "Buyer" };
            if (!validRoles.Contains(request.Role))
                return BadRequest($"Invalid role. Valid roles are: {string.Join(", ", validRoles)}");

            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound("User not found.");

            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId == id.ToString())
                return BadRequest("You cannot change your own role.");

            var oldRole = user.Role;
            user.Role = request.Role;
            user.UpdatedAt = DateTime.UtcNow;

            _db.Users.Update(user);
            await _db.SaveChangesAsync();

            // Send notification to user
            await _notificationService.CreateNotificationAsync(
                id,
                "Role Updated",
                $"Your account role has been updated from {oldRole} to {request.Role}.",
                "RoleChanged",
                null
            );

            _logger.LogInformation($"User {id} role changed from {oldRole} to {request.Role} by admin");

            return Ok(new
            {
                message = "User role updated successfully",
                user = new
                {
                    user.Id,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.Role
                }
            });
        }

        // DELETE: api/Admin/users/{id}
        [HttpDelete("users/{id:int}")]
        public async Task<IActionResult> DeleteUser(int id, [FromBody] DeleteUserRequest? request)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId == id.ToString())
                return BadRequest("You cannot delete your own account.");

            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound("User not found.");

            var reason = request?.Reason ?? "Administrative action";

            // Check for active auctions
            var activeProducts = await _db.Products
                .Where(p => p.SellerId == id && p.Status == "Active")
                .CountAsync();

            if (activeProducts > 0 && !(request?.ForceDelete ?? false))
            {
                return BadRequest(new
                {
                    message = "User has active auctions. Set forceDelete to true to delete anyway.",
                    activeAuctions = activeProducts
                });
            }

            // Get all user's products
            var userProducts = await _db.Products
                .Include(p => p.Images)
                .Where(p => p.SellerId == id)
                .ToListAsync();

            // Delete all product images
            foreach (var product in userProducts)
            {
                foreach (var image in product.Images)
                {
                    await _imageUploadService.DeleteImageAsync(image.ImageUrl);
                }
            }

            // Notify bidders of cancelled auctions
            var affectedBidders = await _db.Bids
                .Where(b => userProducts.Select(p => p.Id).Contains(b.ProductId))
                .Select(b => b.BuyerId)
                .Distinct()
                .ToListAsync();

            foreach (var bidderId in affectedBidders)
            {
                await _notificationService.CreateNotificationAsync(
                    bidderId,
                    "Auction Cancelled",
                    "An auction you bid on has been cancelled due to seller account deletion.",
                    "AuctionCancelled",
                    null
                );
            }

            // Delete user (cascading will handle related data)
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            _logger.LogWarning($"User {id} ({user.Email}) deleted by admin. Reason: {reason}. Products affected: {userProducts.Count}");

            return Ok(new
            {
                message = "User account deleted successfully",
                deletedProducts = userProducts.Count,
                reason
            });
        }

        #endregion

        #region Dashboard Statistics

        // GET: api/Admin/dashboard/stats
        [HttpGet("dashboard/stats")]
        public async Task<IActionResult> GetDashboardStats()
        {
            var totalUsers = await _db.Users.CountAsync();
            var totalProducts = await _db.Products.CountAsync();
            var pendingProducts = await _db.Products.CountAsync(p => !p.IsApproved && p.Status == "Pending");
            var activeAuctions = await _db.Products.CountAsync(p => p.IsApproved && p.Status == "Active");
            var totalBids = await _db.Bids.CountAsync();

            var usersByRole = await _db.Users
                .GroupBy(u => u.Role)
                .Select(g => new { role = g.Key, count = g.Count() })
                .ToListAsync();

            var productsByStatus = await _db.Products
                .GroupBy(p => p.Status)
                .Select(g => new { status = g.Key, count = g.Count() })
                .ToListAsync();

            var recentUsers = await _db.Users
                .OrderByDescending(u => u.CreatedAt)
                .Take(5)
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.Role,
                    u.CreatedAt
                })
                .ToListAsync();

            var recentProducts = await _db.Products
                .Include(p => p.Seller)
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Status,
                    p.IsApproved,
                    p.CreatedAt,
                    Seller = new { p.Seller!.FirstName, p.Seller.LastName, p.Seller.Email }
                })
                .ToListAsync();

            return Ok(new
            {
                overview = new
                {
                    totalUsers,
                    totalProducts,
                    pendingProducts,
                    activeAuctions,
                    totalBids
                },
                usersByRole,
                productsByStatus,
                recentUsers,
                recentProducts
            });
        }

        #endregion

        #region DTOs

        public record ApproveProductRequest(string? Comment);
        public record RejectProductRequest(string Reason);
        public record DeleteProductRequest(string? Reason);
        public record UpdateRoleRequest(string Role);
        public record DeleteUserRequest(string? Reason, bool ForceDelete);

        #endregion
    }
}

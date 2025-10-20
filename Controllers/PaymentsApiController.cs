using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SA_Project_API.Data;
using SA_Project_API.Services;
using System.Security.Claims;

namespace SA_Project_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PaymentsApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IPaymentService _paymentService;
        private readonly ILogger<PaymentsApiController> _logger;

        public PaymentsApiController(AppDbContext db, IPaymentService paymentService, ILogger<PaymentsApiController> logger)
        {
            _db = db;
            _paymentService = paymentService;
            _logger = logger;
        }

        // POST: api/Payments/process
        [HttpPost("process")]
        public async Task<IActionResult> ProcessPayment(ProcessPaymentRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            // Verify order exists and belongs to user
            var order = await _db.Orders.FindAsync(request.OrderId);
            if (order == null)
                return NotFound("Order not found.");

            // Only buyer or admin can process payment
            if (order.BuyerId != userId && !User.IsInRole("Admin"))
                return Forbid("You can only process payments for your own orders.");

            var paymentRequest = new PaymentRequest(
                request.PaymentMethod ?? "CreditCard",
                request.CardNumber,
                request.CardHolderName,
                request.ExpiryDate,
                request.CVV
            );

            var result = await _paymentService.ProcessPaymentAsync(request.OrderId, paymentRequest);

            if (!result.IsSuccess)
            {
                return BadRequest(new { message = result.Message });
            }

            return Ok(new
            {
                success = true,
                message = result.Message,
                payment = result.Payment,
                transactionId = result.Payment?.TransactionId
            });
        }

        // GET: api/Payments/order/{orderId}
        [HttpGet("order/{orderId:int}")]
        public async Task<IActionResult> GetPaymentByOrderId(int orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var payment = await _db.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.OrderId == orderId);

            if (payment == null)
                return NotFound("Payment not found for this order.");

            // Only buyer, seller, or admin can view payment
            var order = payment.Order;
            if (order != null)
            {
                var product = await _db.Products.FindAsync(order.ProductId);
                if (order.BuyerId != userId && product?.SellerId != userId && !User.IsInRole("Admin"))
                    return Forbid("You do not have permission to view this payment.");
            }

            return Ok(payment);
        }

        // GET: api/Payments/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetPaymentById(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var payment = await _db.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null)
                return NotFound();

            // Only buyer, seller, or admin can view payment
            var order = payment.Order;
            if (order != null)
            {
                var product = await _db.Products.FindAsync(order.ProductId);
                if (order.BuyerId != userId && product?.SellerId != userId && !User.IsInRole("Admin"))
                    return Forbid("You do not have permission to view this payment.");
            }

            return Ok(payment);
        }

        // GET: api/Payments/my-payments
        [HttpGet("my-payments")]
        public async Task<IActionResult> GetMyPayments([FromQuery] int limit = 50)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var payments = await _db.Payments
                .Include(p => p.Order)
                    .ThenInclude(o => o!.Product)
                .Where(p => p.Order!.BuyerId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(payments);
        }

        // POST: api/Payments/{id}/refund
        [HttpPost("{id:int}/refund")]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> RefundPayment(int id, RefundRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var payment = await _db.Payments
                .Include(p => p.Order)
                    .ThenInclude(o => o!.Product)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null)
                return NotFound("Payment not found.");

            // Only seller or admin can refund
            if (payment.Order?.Product?.SellerId != userId && !User.IsInRole("Admin"))
                return Forbid("Only the seller or admin can refund payments.");

            var result = await _paymentService.RefundPaymentAsync(id, request.Reason ?? "Refund requested");

            if (!result.IsSuccess)
            {
                return BadRequest(new { message = result.Message });
            }

            return Ok(new
            {
                success = true,
                message = result.Message,
                payment = result.Payment
            });
        }

        // GET: api/Payments/test-gateway
        [HttpGet("test-gateway")]
        [AllowAnonymous]
        public IActionResult TestGateway()
        {
            return Ok(new
            {
                status = "online",
                message = "Dummy payment gateway is operational",
                supportedMethods = new[] { "CreditCard", "PayPal", "BankTransfer" },
                successRate = "95%",
                testMode = true
            });
        }

        // DTOs
        public record ProcessPaymentRequest(
            int OrderId,
            string? PaymentMethod,
            string? CardNumber,
            string? CardHolderName,
            string? ExpiryDate,
            string? CVV
        );

        public record RefundRequest(string? Reason);
    }
}

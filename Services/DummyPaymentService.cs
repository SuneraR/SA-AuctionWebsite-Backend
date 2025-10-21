using SA_Project_API.Data;
using SA_Project_API.Models;
using Microsoft.EntityFrameworkCore;

namespace SA_Project_API.Services
{
    public interface IPaymentService
    {
        Task<PaymentResult> ProcessPaymentAsync(int orderId, PaymentRequest paymentRequest);
        Task<PaymentResult> RefundPaymentAsync(int paymentId, string reason);
        Task<Payment?> GetPaymentByOrderIdAsync(int orderId);
    }

    public class DummyPaymentService : IPaymentService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<DummyPaymentService> _logger;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;

        public DummyPaymentService(AppDbContext db, ILogger<DummyPaymentService> logger, INotificationService notificationService, IEmailService emailService)
        {
            _db = db;
            _logger = logger;
            _notificationService = notificationService;
            _emailService = emailService;
        }

        public async Task<PaymentResult> ProcessPaymentAsync(int orderId, PaymentRequest paymentRequest)
        {
            try
            {
                // Get order with product and buyer
                var order = await _db.Orders
                    .Include(o => o.Product)
                    .Include(o => o.Buyer)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null)
                {
                    return PaymentResult.CreateFailure("Order not found.");
                }

                if (order.Status != "Pending")
                {
                    return PaymentResult.CreateFailure($"Cannot process payment for order with status: {order.Status}");
                }

                // Check if payment already exists
                var existingPayment = await _db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
                if (existingPayment != null)
                {
                    return PaymentResult.CreateFailure("Payment already processed for this order.");
                }

                // Simulate payment processing delay
                await Task.Delay(1000);

                // Dummy validation: Simulate 95% success rate
                var random = new Random();
                var isSuccess = random.Next(100) < 95;

                if (!isSuccess)
                {
                    // Create failed payment record
                    var failedPayment = new Payment
                    {
                        OrderId = orderId,
                        Amount = order.FinalPrice,
                        PaymentMethod = paymentRequest.PaymentMethod,
                        Status = "Failed",
                        TransactionId = null,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.Payments.Add(failedPayment);
                    await _db.SaveChangesAsync();

                    _logger.LogWarning($"Payment failed for order {orderId}");
                    return PaymentResult.CreateFailure("Payment processing failed. Please try again.");
                }

                // Generate dummy transaction ID
                var transactionId = $"TXN-{DateTime.UtcNow:yyyyMMddHHmmss}-{random.Next(10000, 99999)}";

                // Create successful payment record
                var payment = new Payment
                {
                    OrderId = orderId,
                    Amount = order.FinalPrice,
                    PaymentMethod = paymentRequest.PaymentMethod,
                    Status = "Completed",
                    TransactionId = transactionId,
                    CreatedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow
                };

                _db.Payments.Add(payment);

                // Update order status to Paid
                order.Status = "Paid";
                _db.Orders.Update(order);

                // Mark product as Sold
                if (order.Product != null)
                {
                    order.Product.Status = "Sold";
                    order.Product.UpdatedAt = DateTime.UtcNow;
                    _db.Products.Update(order.Product);
                }

                await _db.SaveChangesAsync();

                // Send in-app notification
                await _notificationService.SendNotificationAsync(
                    order.BuyerId,
                    "OrderPaid",
                    $"Payment of ${order.FinalPrice:F2} completed successfully for order #{orderId}. Transaction ID: {transactionId}"
                );

                // Send email confirmation
                if (order.Buyer != null)
                {
                    await _emailService.SendPaymentConfirmationEmailAsync(
                        order.Buyer.Email,
                        order.Buyer.FirstName,
                        orderId,
                        order.FinalPrice,
                        transactionId
                    );
                }

                _logger.LogInformation($"Payment successful for order {orderId}. Transaction ID: {transactionId}");

                return PaymentResult.CreateSuccess(payment, "Payment processed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing payment for order {orderId}");
                return PaymentResult.CreateFailure("An error occurred while processing payment.");
            }
        }

        public async Task<PaymentResult> RefundPaymentAsync(int paymentId, string reason)
        {
            try
            {
                var payment = await _db.Payments
                    .Include(p => p.Order)
                        .ThenInclude(o => o!.Buyer)
                    .FirstOrDefaultAsync(p => p.Id == paymentId);

                if (payment == null)
                {
                    return PaymentResult.CreateFailure("Payment not found.");
                }

                if (payment.Status != "Completed")
                {
                    return PaymentResult.CreateFailure("Only completed payments can be refunded.");
                }

                // Simulate refund processing
                await Task.Delay(500);

                payment.Status = "Refunded";
                _db.Payments.Update(payment);

                // Update order status
                if (payment.Order != null)
                {
                    payment.Order.Status = "Cancelled";
                    _db.Orders.Update(payment.Order);
                }

                await _db.SaveChangesAsync();

                // Send notification
                if (payment.Order != null)
                {
                    await _notificationService.SendNotificationAsync(
                        payment.Order.BuyerId,
                        "PaymentRefunded",
                        $"Payment of ${payment.Amount:F2} has been refunded for order #{payment.OrderId}. Reason: {reason}"
                    );
                }

                _logger.LogInformation($"Payment {paymentId} refunded. Reason: {reason}");

                return PaymentResult.CreateSuccess(payment, "Payment refunded successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error refunding payment {paymentId}");
                return PaymentResult.CreateFailure("An error occurred while processing refund.");
            }
        }

        public async Task<Payment?> GetPaymentByOrderIdAsync(int orderId)
        {
            return await _db.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.OrderId == orderId);
        }
    }

    // DTOs
    public record PaymentRequest(string PaymentMethod, string? CardNumber, string? CardHolderName, string? ExpiryDate, string? CVV);

    public class PaymentResult
    {
        public bool IsSuccess { get; set; }
        public Payment? Payment { get; set; }
        public string Message { get; set; } = string.Empty;

        public static PaymentResult CreateSuccess(Payment payment, string message)
        {
            return new PaymentResult { IsSuccess = true, Payment = payment, Message = message };
        }

        public static PaymentResult CreateFailure(string message)
        {
            return new PaymentResult { IsSuccess = false, Message = message };
        }
    }
}

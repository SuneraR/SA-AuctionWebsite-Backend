using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace SA_Project_API.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody);
        Task SendWelcomeEmailAsync(string email, string firstName);
        Task SendAuctionWonEmailAsync(string email, string firstName, string productName, decimal finalPrice);
        Task SendBidPlacedEmailAsync(string email, string firstName, string productName, decimal bidAmount);
        Task SendBidOutbidEmailAsync(string email, string firstName, string productName);
        Task SendPaymentConfirmationEmailAsync(string email, string firstName, int orderId, decimal amount, string transactionId);
        Task SendPaymentReminderEmailAsync(string email, string firstName, int orderId, decimal amount);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody)
        {
            try
            {
                var emailSettings = _configuration.GetSection("EmailSettings");
                var fromEmail = emailSettings["FromEmail"];
                var fromName = emailSettings["FromName"];
                var smtpServer = emailSettings["SmtpServer"];
                var smtpPort = int.Parse(emailSettings["SmtpPort"] ?? "587");
                var smtpUsername = emailSettings["SmtpUsername"];
                var smtpPassword = emailSettings["SmtpPassword"];
                var enableSsl = bool.Parse(emailSettings["EnableSsl"] ?? "true");

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(fromName, fromEmail));
                message.To.Add(new MailboxAddress(toName, toEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder
                {
                    HtmlBody = htmlBody
                };
                message.Body = bodyBuilder.ToMessageBody();

                using var client = new SmtpClient();
                await client.ConnectAsync(smtpServer, smtpPort, enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);
                await client.AuthenticateAsync(smtpUsername, smtpPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation($"Email sent successfully to {toEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send email to {toEmail}");
                // Don't throw - we don't want email failures to break the application
            }
        }

        public async Task SendWelcomeEmailAsync(string email, string firstName)
        {
            var subject = "Welcome to Auction Platform!";
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; padding: 10px 20px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Welcome to Auction Platform!</h1>
        </div>
        <div class='content'>
            <h2>Hi {firstName},</h2>
            <p>Thank you for registering with us! We're excited to have you join our auction community.</p>
            <p>You can now:</p>
            <ul>
                <li>Browse active auctions</li>
                <li>Place bids on items you're interested in</li>
                <li>List your own items for auction (if you're a seller)</li>
                <li>Track your bids and orders</li>
            </ul>
            <p>Happy bidding!</p>
        </div>
        <div class='footer'>
            <p>&copy; 2025 Auction Platform. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(email, firstName, subject, htmlBody);
        }

        public async Task SendAuctionWonEmailAsync(string email, string firstName, string productName, decimal finalPrice)
        {
            var subject = $"Congratulations! You won: {productName}";
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #FF9800; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .highlight {{ background-color: #FFF3CD; padding: 15px; border-left: 4px solid #FF9800; margin: 20px 0; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>?? Congratulations!</h1>
        </div>
        <div class='content'>
            <h2>Hi {firstName},</h2>
            <p>Great news! You have won the auction for:</p>
            <div class='highlight'>
                <h3>{productName}</h3>
                <p><strong>Final Price:</strong> ${finalPrice:F2}</p>
            </div>
            <p>An order has been created for you. Please complete the payment to secure your purchase.</p>
            <p>You can view your order details in your account dashboard.</p>
            <p>Thank you for participating in our auction!</p>
        </div>
        <div class='footer'>
            <p>&copy; 2025 Auction Platform. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(email, firstName, subject, htmlBody);
        }

        public async Task SendBidPlacedEmailAsync(string email, string firstName, string productName, decimal bidAmount)
        {
            var subject = $"Bid Confirmed: {productName}";
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #2196F3; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Bid Placed Successfully</h1>
        </div>
        <div class='content'>
            <h2>Hi {firstName},</h2>
            <p>Your bid has been placed successfully!</p>
            <p><strong>Product:</strong> {productName}</p>
            <p><strong>Your Bid:</strong> ${bidAmount:F2}</p>
            <p>We'll notify you if you're outbid or when the auction ends.</p>
            <p>Good luck!</p>
        </div>
        <div class='footer'>
            <p>&copy; 2025 Auction Platform. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(email, firstName, subject, htmlBody);
        }

        public async Task SendBidOutbidEmailAsync(string email, string firstName, string productName)
        {
            var subject = $"You've been outbid on: {productName}";
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #F44336; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; padding: 10px 20px; background-color: #F44336; color: white; text-decoration: none; border-radius: 5px; margin-top: 10px; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>You've Been Outbid!</h1>
        </div>
        <div class='content'>
            <h2>Hi {firstName},</h2>
            <p>Someone has placed a higher bid on:</p>
            <p><strong>{productName}</strong></p>
            <p>Don't miss out! Place a higher bid to stay in the running.</p>
        </div>
        <div class='footer'>
            <p>&copy; 2025 Auction Platform. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(email, firstName, subject, htmlBody);
        }

        public async Task SendPaymentConfirmationEmailAsync(string email, string firstName, int orderId, decimal amount, string transactionId)
        {
            var subject = "Payment Confirmed - Order Receipt";
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .receipt {{ background-color: white; padding: 20px; border: 1px solid #ddd; margin: 20px 0; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>? Payment Confirmed</h1>
        </div>
        <div class='content'>
            <h2>Hi {firstName},</h2>
            <p>Your payment has been received and confirmed.</p>
            <div class='receipt'>
                <h3>Receipt</h3>
                <p><strong>Order ID:</strong> #{orderId}</p>
                <p><strong>Amount Paid:</strong> ${amount:F2}</p>
                <p><strong>Transaction ID:</strong> {transactionId}</p>
                <p><strong>Date:</strong> {DateTime.UtcNow:MMMM dd, yyyy HH:mm} UTC</p>
            </div>
            <p>Thank you for your purchase!</p>
        </div>
        <div class='footer'>
            <p>&copy; 2025 Auction Platform. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(email, firstName, subject, htmlBody);
        }

        public async Task SendPaymentReminderEmailAsync(string email, string firstName, int orderId, decimal amount)
        {
            var subject = "Payment Reminder - Complete Your Purchase";
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #FF9800; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; padding: 12px 24px; background-color: #FF9800; color: white; text-decoration: none; border-radius: 5px; margin-top: 15px; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Payment Reminder</h1>
        </div>
        <div class='content'>
            <h2>Hi {firstName},</h2>
            <p>This is a friendly reminder that you have a pending payment.</p>
            <p><strong>Order ID:</strong> #{orderId}</p>
            <p><strong>Amount Due:</strong> ${amount:F2}</p>
            <p>Please complete your payment to secure your purchase.</p>
        </div>
        <div class='footer'>
            <p>&copy; 2025 Auction Platform. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

            await SendEmailAsync(email, firstName, subject, htmlBody);
        }
    }
}

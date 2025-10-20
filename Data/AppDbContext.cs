using Microsoft.EntityFrameworkCore;
using SA_Project_API.Models;

namespace SA_Project_API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Bid> Bids { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(u => u.Email).IsRequired().HasMaxLength(255);
                entity.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
                entity.Property(u => u.PasswordSalt).IsRequired().HasMaxLength(512);
                entity.Property(u => u.Role).IsRequired().HasMaxLength(20).HasDefaultValue("Buyer");

                entity.Property(u => u.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(u => u.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

                entity.HasIndex(u => u.Email).IsUnique();
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.Property(p => p.Name).IsRequired().HasMaxLength(255);
                entity.Property(p => p.StartPrice).HasColumnType("decimal(10,2)");
                entity.Property(p => p.CurrentPrice).HasColumnType("decimal(10,2)");
                entity.Property(p => p.MinBidIncrement).HasColumnType("decimal(10,2)").HasDefaultValue(1.00m);
                entity.Property(p => p.Status).IsRequired().HasMaxLength(20).HasDefaultValue("Draft");

                entity.Property(p => p.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(p => p.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

                entity.HasOne(p => p.Seller)
                      .WithMany()
                      .HasForeignKey(p => p.SellerId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(p => p.Winner)
                      .WithMany()
                      .HasForeignKey(p => p.WinnerId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasCheckConstraint("CK_Product_Times", "StartTime < EndTime");
            });

            modelBuilder.Entity<Bid>(entity =>
            {
                entity.Property(b => b.BidAmount).IsRequired().HasColumnType("decimal(10,2)");
                entity.Property(b => b.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(b => b.Product)
                      .WithMany()
                      .HasForeignKey(b => b.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(b => b.Buyer)
                      .WithMany()
                      .HasForeignKey(b => b.BuyerId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.Property(o => o.FinalPrice).IsRequired().HasColumnType("decimal(10,2)");
                entity.Property(o => o.Status).IsRequired().HasMaxLength(20).HasDefaultValue("Pending");
                entity.Property(o => o.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(o => o.Product)
                      .WithMany()
                      .HasForeignKey(o => o.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(o => o.Buyer)
                      .WithMany()
                      .HasForeignKey(o => o.BuyerId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Notification>(entity =>
            {
                entity.Property(n => n.Type).IsRequired().HasMaxLength(50);
                entity.Property(n => n.Message).IsRequired();
                entity.Property(n => n.IsRead).HasDefaultValue(false);
                entity.Property(n => n.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(n => n.User)
                      .WithMany()
                      .HasForeignKey(n => n.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.Property(p => p.Amount).IsRequired().HasColumnType("decimal(10,2)");
                entity.Property(p => p.PaymentMethod).IsRequired().HasMaxLength(50);
                entity.Property(p => p.Status).IsRequired().HasMaxLength(20).HasDefaultValue("Pending");
                entity.Property(p => p.TransactionId).HasMaxLength(255);
                entity.Property(p => p.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(p => p.Order)
                      .WithMany()
                      .HasForeignKey(p => p.OrderId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}

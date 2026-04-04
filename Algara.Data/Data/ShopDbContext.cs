using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Data
{
    public class ShopDbContext : DbContext
    {
        public ShopDbContext(DbContextOptions<ShopDbContext> options)
            : base(options) { }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // --- Category ---
            modelBuilder.Entity<Category>(b =>
            {
                b.HasKey(c => c.N);
                b.Property(c => c.N).UseIdentityColumn();
                b.ToTable("Categories");
                b.Property(c => c.Name).IsRequired().HasMaxLength(200);
            });

            // --- Product ---
            modelBuilder.Entity<Product>(b =>
            {
                b.HasKey(p => p.N);
                b.Property(p => p.N).UseIdentityColumn();
                b.ToTable("Products");
                b.Property(p => p.Name).IsRequired().HasMaxLength(300);
                b.Property(p => p.Price).HasColumnType("decimal(18,2)");

                b.HasOne(p => p.Category)
                 .WithMany(c => c.Products)
                 .HasForeignKey(p => p.CategoryN)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // --- Order ---
            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(o => o.N);
                b.Property(o => o.N).UseIdentityColumn();
                b.ToTable("Orders");
                b.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");

                // FK към Users.N е от различен DbContext — добавя се ръчно в миграцията.
                b.HasIndex(o => o.UserN, "IX_Orders_UserN");
            });

            // --- OrderItem ---
            modelBuilder.Entity<OrderItem>(b =>
            {
                b.HasKey(oi => oi.N);
                b.Property(oi => oi.N).UseIdentityColumn();
                b.ToTable("OrderItems");
                b.Property(oi => oi.UnitPrice).HasColumnType("decimal(18,2)");

                b.HasOne(oi => oi.Order)
                 .WithMany(o => o.OrderItems)
                 .HasForeignKey(oi => oi.OrderN)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(oi => oi.Product)
                 .WithMany(p => p.OrderItems)
                 .HasForeignKey(oi => oi.ProductN)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}

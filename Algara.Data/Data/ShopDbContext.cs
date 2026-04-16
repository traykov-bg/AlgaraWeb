using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Data
{
    public class ShopDbContext : DbContext
    {
        public ShopDbContext(DbContextOptions<ShopDbContext> options)
            : base(options) { }

        public DbSet<Category> Categories { get; set; }
        public DbSet<SubCategory> SubCategories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductSubCategory> ProductSubCategories { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<HeroSlide> HeroSlides { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<ProductPromotion> ProductPromotions { get; set; }

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

            // --- SubCategory ---
            modelBuilder.Entity<SubCategory>(b =>
            {
                b.HasKey(sc => sc.N);
                b.Property(sc => sc.N).UseIdentityColumn();
                b.ToTable("SubCategories");
                b.Property(sc => sc.Name).IsRequired().HasMaxLength(200);
                b.Property(sc => sc.Slug).IsRequired().HasMaxLength(200);

                b.HasOne(sc => sc.Category)
                 .WithMany(c => c.SubCategories)
                 .HasForeignKey(sc => sc.CategoryN)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // --- ProductSubCategory ---
            modelBuilder.Entity<ProductSubCategory>(b =>
            {
                b.HasKey(psc => new { psc.ProductN, psc.SubCategoryN });
                b.ToTable("ProductSubCategories");

                b.HasOne(psc => psc.Product)
                 .WithMany(p => p.ProductSubCategories)
                 .HasForeignKey(psc => psc.ProductN)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(psc => psc.SubCategory)
                 .WithMany(sc => sc.ProductSubCategories)
                 .HasForeignKey(psc => psc.SubCategoryN)
                 .OnDelete(DeleteBehavior.Cascade);
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

            // --- Promotion ---
            modelBuilder.Entity<Promotion>(b =>
            {
                b.HasKey(pr => pr.N);
                b.Property(pr => pr.N).UseIdentityColumn();
                b.ToTable("Promotions");
                b.Property(pr => pr.Name).IsRequired().HasMaxLength(300);
                b.Property(pr => pr.Type).HasConversion<int>();
            });

            // --- ProductPromotion ---
            modelBuilder.Entity<ProductPromotion>(b =>
            {
                b.HasKey(pp => new { pp.ProductN, pp.PromotionN });
                b.ToTable("ProductPromotions");

                b.Property(pp => pp.OriginalPrice).HasColumnType("decimal(18,2)");
                b.Property(pp => pp.PromoPrice).HasColumnType("decimal(18,2)");
                b.Property(pp => pp.DiscountPercent).HasColumnType("decimal(5,3)");
                b.Property(pp => pp.Note).HasMaxLength(500);
                b.Ignore(pp => pp.DiscountAmount);

                b.HasOne(pp => pp.Product)
                 .WithMany(p => p.ProductPromotions)
                 .HasForeignKey(pp => pp.ProductN)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(pp => pp.Promotion)
                 .WithMany(pr => pr.ProductPromotions)
                 .HasForeignKey(pp => pp.PromotionN)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // --- HeroSlide ---
            modelBuilder.Entity<HeroSlide>(b =>
            {
                b.HasKey(s => s.N);
                b.Property(s => s.N).UseIdentityColumn();
                b.ToTable("HeroSlides");
                b.Property(s => s.ImageUrl).IsRequired().HasMaxLength(2048);
                b.Property(s => s.EyebrowText).HasMaxLength(200);
                b.Property(s => s.Title).IsRequired().HasMaxLength(300);
                b.Property(s => s.Subtitle).HasMaxLength(500);
                b.Property(s => s.ButtonText).IsRequired().HasMaxLength(100);
                b.Property(s => s.ButtonUrl).IsRequired().HasMaxLength(2048);
                b.HasIndex(s => new { s.IsActive, s.DisplayOrder })
                 .HasDatabaseName("IX_HeroSlides_Active_Order");
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}

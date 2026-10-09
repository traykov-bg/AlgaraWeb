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
        public DbSet<ProductTranslation> ProductTranslations { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>("ProductSkuNumbers").StartsAt(1).IncrementsBy(1);

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
                b.ToTable("Products", table =>
                {
                    table.HasCheckConstraint("CK_Products_Sku", "DATALENGTH([Sku]) BETWEEN 2 AND 128 AND [Sku] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^-A-Z0-9_]%'");
                    table.HasCheckConstraint("CK_Products_WidthCm", "[WidthCm] IS NULL OR ([WidthCm] > 0 AND [WidthCm] <= 100000)");
                    table.HasCheckConstraint("CK_Products_HeightCm", "[HeightCm] IS NULL OR ([HeightCm] > 0 AND [HeightCm] <= 100000)");
                    table.HasCheckConstraint("CK_Products_DepthCm", "[DepthCm] IS NULL OR ([DepthCm] > 0 AND [DepthCm] <= 100000)");
                });
                b.Property(p => p.Sku).IsRequired().HasMaxLength(ProductSku.MaxLength)
                    .UseCollation("Latin1_General_100_CI_AS");
                b.HasIndex(p => p.Sku).IsUnique().HasDatabaseName("IX_Products_Sku");
                b.Property(p => p.Name).IsRequired().HasMaxLength(300);
                b.Property(p => p.Price).HasColumnType("decimal(18,2)");
                b.Property(p => p.WidthCm).HasColumnType("decimal(10,2)");
                b.Property(p => p.HeightCm).HasColumnType("decimal(10,2)");
                b.Property(p => p.DepthCm).HasColumnType("decimal(10,2)");

                b.HasOne(p => p.Category)
                 .WithMany(c => c.Products)
                 .HasForeignKey(p => p.CategoryN)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ProductTranslation>(b =>
            {
                b.HasKey(t => new { t.ProductN, t.Language });
                b.ToTable("ProductTranslations", table => table.HasCheckConstraint(
                    "CK_ProductTranslations_Language", "[Language] COLLATE Latin1_General_100_BIN2 = N'en'"));
                b.Property(t => t.Language).IsRequired().HasMaxLength(2);
                b.Property(t => t.Name).HasMaxLength(300);
                b.HasOne(t => t.Product).WithMany(p => p.Translations)
                    .HasForeignKey(t => t.ProductN).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ProductImage>(b =>
            {
                b.HasKey(i => i.N);
                b.Property(i => i.N).UseIdentityColumn();
                b.ToTable("ProductImages", table => table.HasCheckConstraint(
                    "CK_ProductImages_DisplayOrder", "[DisplayOrder] >= 0"));
                b.Property(i => i.ImageUrl).IsRequired().HasMaxLength(2048);
                b.HasIndex(i => new { i.ProductN, i.DisplayOrder });
                b.HasOne(i => i.Product).WithMany(p => p.Images)
                    .HasForeignKey(i => i.ProductN).OnDelete(DeleteBehavior.Cascade);
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

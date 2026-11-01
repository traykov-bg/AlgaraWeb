using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Data;

public partial class ShopDbContext
{
    public DbSet<ProductShippingParcel> ProductShippingParcels { get; set; }

    private static void ConfigureProductShipping(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().Property(p => p.AllowsCourier).HasDefaultValue(false);
        modelBuilder.Entity<Product>().Property(p => p.UseCatalogDimensionsForShipping).HasDefaultValue(false);
        modelBuilder.Entity<ProductShippingParcel>(b =>
        {
            b.HasKey(p => p.N);
            b.Property(p => p.N).UseIdentityColumn();
            b.Property(p => p.WeightKg).HasPrecision(9, 3);
            b.Property(p => p.WidthCm).HasPrecision(10, 2);
            b.Property(p => p.HeightCm).HasPrecision(10, 2);
            b.Property(p => p.DepthCm).HasPrecision(10, 2);
            b.HasOne(p => p.Product).WithMany(p => p.ShippingParcels).HasForeignKey(p => p.ProductN).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(p => new { p.ProductN, p.Position }).IsUnique();
            b.ToTable("ProductShippingParcels", table =>
            {
                table.HasCheckConstraint("CK_ProductShippingParcels_Position", "[Position] >= 1 AND [Position] <= 50");
                table.HasCheckConstraint("CK_ProductShippingParcels_Weight", "[WeightKg] >= 0.001 AND [WeightKg] <= 100000");
                table.HasCheckConstraint("CK_ProductShippingParcels_Dimensions",
                    "([WidthCm] IS NULL AND [HeightCm] IS NULL AND [DepthCm] IS NULL) OR " +
                    "([WidthCm] IS NOT NULL AND [HeightCm] IS NOT NULL AND [DepthCm] IS NOT NULL AND " +
                    "[WidthCm] >= 0.01 AND [WidthCm] <= 100000 AND [HeightCm] >= 0.01 AND [HeightCm] <= 100000 AND [DepthCm] >= 0.01 AND [DepthCm] <= 100000)");
            });
        });
    }
}

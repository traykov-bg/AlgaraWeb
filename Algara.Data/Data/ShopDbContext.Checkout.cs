using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Data;

public partial class ShopDbContext
{
    private static void ConfigureCheckout(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEvent>(b =>
        {
            b.ToTable("OrderEvents");
            b.HasKey(e => e.N);
            b.Property(e => e.Action).HasMaxLength(40);
            b.Property(e => e.ActorId).HasMaxLength(128);
            b.HasOne(e => e.Order).WithMany().HasForeignKey(e => e.OrderN).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => new { e.OrderN, e.OccurredAtUtc });
        });
        modelBuilder.Entity<Order>(b =>
        {
            b.HasIndex(o => o.SubmissionId).IsUnique().HasFilter("[SubmissionId] IS NOT NULL");
            b.HasIndex(o => new { o.Status, o.ExpiresAtUtc });
            b.Property(o => o.GuestTokenHash).HasMaxLength(64);
            b.Property(o => o.ContactName).HasMaxLength(121);
            b.Property(o => o.ContactEmail).HasMaxLength(256);
            b.Property(o => o.ContactPhone).HasMaxLength(20);
            b.Property(o => o.DeliverySnapshot).HasMaxLength(4000);
            b.Property(o => o.Comment).HasMaxLength(2000);
            b.Property(o => o.InvoiceDetails).HasMaxLength(1000);
            b.Property(o => o.DeliveryAmount).HasColumnType("decimal(18,2)");
            b.Property(o => o.RowVersion).IsRowVersion();
        });
        modelBuilder.Entity<OrderItem>(b =>
        {
            b.Property(i => i.ProductName).HasMaxLength(300);
            b.Property(i => i.VariantName).HasMaxLength(500);
            b.Property(i => i.Sku).HasMaxLength(64);
            b.HasIndex(i => new { i.ProductN, i.VariantN });
            b.ToTable("OrderItems", t => t.HasCheckConstraint("CK_OrderItems_ReservedQuantity", "[ReservedQuantity] >= 0 AND [ReservedQuantity] <= [Quantity]"));
        });
    }
}

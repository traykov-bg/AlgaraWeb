using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
namespace Algara.Data.Data;
public partial class ShopDbContext
{
    private static void ConfigureInquiries(ModelBuilder model)
    {
        model.Entity<FurnitureInquiry>(b => {
            b.ToTable("FurnitureInquiries", t => t.HasCheckConstraint("CK_FurnitureInquiries_Status", "[Status] BETWEEN 0 AND 6"));
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderN).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.OrderN).IsUnique().HasFilter("[OrderN] IS NOT NULL");
            b.HasKey(x => x.N); b.HasIndex(x => x.SubmissionId).IsUnique();
            b.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc }); b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.GuestTokenHash).HasMaxLength(64);
            b.Property(x => x.CustomerId).HasMaxLength(128); b.Property(x => x.CreatedByStaffId).HasMaxLength(128);
            b.Property(x => x.AssignedStaffId).HasMaxLength(128); b.Property(x => x.Channel).HasMaxLength(20);
            b.Property(x => x.ContactName).HasMaxLength(121); b.Property(x => x.Phone).HasMaxLength(20);
            b.Property(x => x.Email).HasMaxLength(256); b.Property(x => x.ProductName).HasMaxLength(300);
            b.Property(x => x.Dimensions).HasMaxLength(500); b.Property(x => x.Materials).HasMaxLength(1000);
            b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.Language).HasMaxLength(2);
            b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductN).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.InquiryN).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.InquiryN).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Offers).WithOne().HasForeignKey(x => x.InquiryN).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Order>(b => {
            b.HasOne<FurnitureInquiry>().WithMany().HasForeignKey(x => x.InquiryN).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<InquiryOffer>().WithMany().HasForeignKey(x => x.InquiryOfferN).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.InquiryN).IsUnique().HasFilter("[InquiryN] IS NOT NULL AND [SplitFromN] IS NULL");
        });
        model.Entity<OrderItem>(b => {
            b.Property(x => x.CatalogUnitPrice).HasPrecision(18,2);
            b.Property(x => x.ExecutionComment).HasMaxLength(6000);
            b.ToTable("OrderItems", t => t.HasCheckConstraint("CK_OrderItems_IndividualReady", "[ReadyQuantity] >= 0 AND [ReadyQuantity] <= [Quantity] AND ([IndividualManufacture] = 1 OR [ReadyQuantity] = 0) AND ([IndividualManufacture] = 0 OR ([ReservedQuantity] = 0 AND [DeductedQuantity] = 0))"));
        });
        model.Entity<InquiryAttachment>(b => {
            b.ToTable("InquiryAttachments", t => t.HasCheckConstraint("CK_InquiryAttachments_Size", "DATALENGTH([Contents]) BETWEEN 1 AND 5242880"));
            b.HasKey(x => x.N); b.Property(x => x.FileName).HasMaxLength(200);
        });
        model.Entity<InquiryEvent>(b => {
            b.ToTable("InquiryEvents"); b.HasKey(x => x.N); b.Property(x => x.ActorId).HasMaxLength(128);
            b.Property(x => x.ActorName).HasMaxLength(200); b.Property(x => x.Action).HasMaxLength(40); b.Property(x => x.Note).HasMaxLength(2000);
        });
        model.Entity<InquiryOffer>(b => {
            b.ToTable("InquiryOffers", t => t.HasCheckConstraint("CK_InquiryOffers_Price", "[Price] > 0 AND [Price] <= 999999999"));
            b.Property(x => x.DeliveryAmount).HasPrecision(18,2);
            b.HasKey(x => x.N); b.Property(x => x.Price).HasPrecision(18,2); b.Property(x => x.Terms).HasMaxLength(1000);
            b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.ActorId).HasMaxLength(128);
        });
    }
}

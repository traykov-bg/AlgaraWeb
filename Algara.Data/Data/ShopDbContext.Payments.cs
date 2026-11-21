using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
namespace Algara.Data.Data;
public partial class ShopDbContext
{
 public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
 public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
 public DbSet<PaymentPolicy> PaymentPolicies => Set<PaymentPolicy>();
 public override int SaveChanges(bool acceptAllChangesOnSuccess)
 {
  ProtectPaymentLedger(); return base.SaveChanges(acceptAllChangesOnSuccess);
 }
 public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken=default)
 {
  ProtectPaymentLedger(); return base.SaveChangesAsync(acceptAllChangesOnSuccess,cancellationToken);
 }
 private void ProtectPaymentLedger()
 {
  if(ChangeTracker.Entries<OrderPayment>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))
   throw new InvalidOperationException("Posted payments are immutable. Add a reversal instead.");
 }
 private static void ConfigurePayments(ModelBuilder model)
 {
  model.Entity<Partner>().ToTable("Partners", t => t.HasCheckConstraint("CK_Partners_PaymentDays", "[DeferredPaymentDays] BETWEEN 0 AND 3650"));
  model.Entity<Order>().Property(o => o.DepositPercent).HasPrecision(5,2);
  model.Entity<Order>().ToTable("Orders", t => {
   t.HasCheckConstraint("CK_Orders_DepositPercent", "[DepositPercent] IS NULL OR [DepositPercent] BETWEEN 0 AND 100");
   t.HasCheckConstraint("CK_Orders_PaymentDays", "[DeferredPaymentDays] IS NULL OR [DeferredPaymentDays] BETWEEN 0 AND 3650");
  });
  model.Entity<PaymentPolicy>(b => {
   b.HasKey(p => p.N); b.Property(p => p.N).ValueGeneratedNever(); b.Property(p => p.DepositPercent).HasPrecision(5,2); b.Property(p => p.RowVersion).IsRowVersion();
   b.ToTable("PaymentPolicies", t => t.HasCheckConstraint("CK_PaymentPolicies_Valid", "[N] = 1 AND [DepositPercent] > 0 AND [DepositPercent] <= 100"));
   b.HasData(new { N=1, DepositPercent=50m });
  });
  model.Entity<PaymentMethod>(b => {
   b.HasKey(p => p.N); b.Property(p => p.Name).HasMaxLength(100); b.Property(p => p.EnglishName).HasMaxLength(100);
   b.HasIndex(p => p.Name).IsUnique(); b.Property(p => p.RowVersion).IsRowVersion();
   b.HasData(new { N=1, Name="В брой", EnglishName="Cash", IsActive=true },new { N=2, Name="Банков превод", EnglishName="Bank transfer", IsActive=true },new { N=3, Name="ПОС", EnglishName="POS card", IsActive=true },new { N=4, Name="Наложен платеж", EnglishName="Cash on delivery", IsActive=true });
  });
  model.Entity<OrderPayment>(b => {
   b.HasKey(p => p.N); b.Property(p => p.Amount).HasPrecision(18,2);
   b.Property(p => p.MethodName).HasMaxLength(100); b.Property(p => p.Location).HasMaxLength(200); b.Property(p => p.ActorId).HasMaxLength(450); b.Property(p => p.ActorName).HasMaxLength(256); b.Property(p => p.Note).HasMaxLength(1000);
   b.HasIndex(p => p.SubmissionId).IsUnique(); b.HasIndex(p => new { p.OrderN,p.N });
   b.HasOne(p => p.Order).WithMany(o => o.Payments).HasForeignKey(p => p.OrderN).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<PaymentMethod>().WithMany().HasForeignKey(p => p.MethodN).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<OrderPayment>().WithMany().HasForeignKey(p => p.ReversesN).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<Order>().WithMany().HasForeignKey(p => p.RelatedOrderN).OnDelete(DeleteBehavior.Restrict);
   b.ToTable("OrderPayments", t => t.HasCheckConstraint("CK_OrderPayments_Amount", "[Amount] <> 0 AND (([Kind] = 0 AND [Amount] > 0 AND [MethodN] IS NOT NULL AND [ReversesN] IS NULL) OR ([Kind] = 1 AND [ReversesN] IS NOT NULL) OR ([Kind] = 2 AND [Amount] < 0 AND [ReversesN] IS NOT NULL) OR ([Kind] = 3 AND [RelatedOrderN] IS NOT NULL))"));
  });
 }
}

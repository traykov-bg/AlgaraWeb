using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Data;

public partial class ShopDbContext
{
    public DbSet<PartnerAccountLink> PartnerAccountLinks { get; set; }
    public DbSet<CustomerDossierEvent> CustomerDossierEvents { get; set; }

    private static void ConfigureCustomerDossiers(ModelBuilder model)
    {
        model.Entity<PartnerAccountLink>(b =>
        {
            b.HasKey(l => l.UserN);
            b.Property(l => l.UserN).ValueGeneratedNever();
            b.Property(l => l.RowVersion).IsRowVersion();
            b.HasOne(l => l.Partner).WithMany().HasForeignKey(l => l.PartnerN)
                .OnDelete(DeleteBehavior.Restrict);
            // The Users.N foreign key belongs to another context and is added in the migration.
        });
        model.Entity<CustomerDossierEvent>(b =>
        {
            b.HasKey(e => e.N);
            b.Property(e => e.N).UseIdentityColumn();
            b.Property(e => e.Action).HasMaxLength(50).IsRequired();
            b.Property(e => e.ActorId).HasMaxLength(128).IsRequired();
            b.Property(e => e.ActorName).HasMaxLength(256).IsRequired();
            b.Property(e => e.Reason).HasMaxLength(2000).IsRequired();
            b.Property(e => e.VerificationMethod).HasMaxLength(40);
            b.Property(e => e.Note).HasMaxLength(4000);
            b.HasIndex(e => new { e.UserN, e.OccurredAtUtc, e.N });
            // Restrict deletion of the existing account instead of cascading away its history.
        });
    }
}

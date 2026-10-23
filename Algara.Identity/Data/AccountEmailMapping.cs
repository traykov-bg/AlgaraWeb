using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Identity.Data;

public static class AccountEmailMapping
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountEmailToken>(b =>
        {
            b.ToTable("AccountEmailTokens", table =>
            {
                table.HasCheckConstraint("CK_AccountEmailTokens_Purpose", "[Purpose] IN (1,2,3)");
                table.HasCheckConstraint("CK_AccountEmailTokens_Target", "([Purpose] IN (1,2) AND [UserN] IS NOT NULL AND [PartnerN] IS NULL AND [ExpectedSecurityStamp] IS NOT NULL) OR ([Purpose]=3 AND [PartnerN] IS NOT NULL AND [PartnerN]>0 AND [CreatedById] IS NOT NULL AND [PartnerVersion] IS NOT NULL AND DATALENGTH([PartnerVersion])=8)");
                table.HasCheckConstraint("CK_AccountEmailTokens_Terminal", "[ConsumedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL");
            });
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Email).HasMaxLength(256).IsRequired();
            b.Property(t => t.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            b.Property(t => t.ExpectedSecurityStamp).HasMaxLength(128);
            b.Property(t => t.CreatedById).HasMaxLength(128);
            b.Property(t => t.CreatedByName).HasMaxLength(256);
            b.Property(t => t.VerificationMethod).HasMaxLength(50);
            b.Property(t => t.Reason).HasMaxLength(2000);
            b.Property(t => t.PartnerVersion).HasMaxLength(8);
            b.Property(t => t.RowVersion).IsRowVersion();
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => new { t.Email, t.Purpose, t.CreatedAtUtc });
            b.HasIndex(t => new { t.PartnerN, t.CreatedAtUtc });
            b.HasIndex(t => new { t.UserN, t.CreatedAtUtc });
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserN).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<AccountEmailAuditEvent>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).UseIdentityColumn();
            b.Property(e => e.ActorId).HasMaxLength(128).IsRequired();
            b.Property(e => e.ActorName).HasMaxLength(256).IsRequired();
            b.Property(e => e.Action).HasMaxLength(60).IsRequired();
            b.Property(e => e.Reason).HasMaxLength(2000).IsRequired();
            b.HasIndex(e => new { e.UserN, e.OccurredAtUtc });
            b.HasIndex(e => new { e.PartnerN, e.OccurredAtUtc });
        });
    }
}

using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Data;

public partial class ShopDbContext
{
    public DbSet<PartnerAccountLink> PartnerAccountLinks { get; set; }
    public DbSet<CustomerDossierEvent> CustomerDossierEvents { get; set; }
    public DbSet<CustomerAssistanceSession> CustomerAssistanceSessions { get; set; }

    private static void ConfigureCustomerDossiers(ModelBuilder model)
    {
        model.Entity<CustomerAssistanceSession>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.ActorId).HasMaxLength(128).IsRequired();
            b.Property(s => s.TargetSecurityVersion).HasMaxLength(44).IsRequired();
            b.Property(s => s.Reason).HasMaxLength(2000).IsRequired();
            b.Property(s => s.EndReason).HasMaxLength(40);
            b.Property(s => s.RowVersion).IsRowVersion();
            b.HasIndex(s => new { s.UserN, s.StartedAtUtc });
            b.HasIndex(s => new { s.ActorId, s.ActorSessionN, s.ExpiresAtUtc });
            b.ToTable("CustomerAssistanceSessions", table =>
            {
                table.HasCheckConstraint("CK_CustomerAssistanceSessions_Mode", "[Mode] IN (1, 2)");
                table.HasCheckConstraint("CK_CustomerAssistanceSessions_Duration", "[ExpiresAtUtc] = DATEADD(minute, 30, [StartedAtUtc])");
                table.HasCheckConstraint("CK_CustomerAssistanceSessions_End", "([EndedAtUtc] IS NULL AND [EndReason] IS NULL) OR ([EndedAtUtc] IS NOT NULL AND [EndedAtUtc] >= [StartedAtUtc] AND [EndedAtUtc] <= [ExpiresAtUtc] AND [EndReason] IS NOT NULL AND [EndReason] IN (N'Expired', N'StaffEnded', N'InvalidContext', N'AccessChanged', N'TargetChanged'))");
                table.HasCheckConstraint("CK_CustomerAssistanceSessions_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
            });
            // Cross-context Users and UserSessions foreign keys are added in the migration with NO ACTION.
        });
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

using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Identity.Data;

public static class EmailOutboxMapping
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailOutboxMessage>(b =>
        {
            b.ToTable("EmailOutboxMessages", table =>
            {
                table.HasCheckConstraint("CK_EmailOutboxMessages_State", "[State] IN ('Pending','Processing','Accepted','Failed','Cancelled')");
                table.HasCheckConstraint("CK_EmailOutboxMessages_Attempts", "[Attempts] BETWEEN 0 AND 3");
                table.HasCheckConstraint("CK_EmailOutboxMessages_Lease", "([State] = 'Processing' AND [LeaseId] IS NOT NULL AND [LeaseUntilUtc] IS NOT NULL) OR ([State] <> 'Processing' AND [LeaseId] IS NULL AND [LeaseUntilUtc] IS NULL)");
                table.HasCheckConstraint("CK_EmailOutboxMessages_Payload", "([State] IN ('Pending','Processing') AND [ProtectedPayload] IS NOT NULL) OR ([State] IN ('Accepted','Failed','Cancelled') AND [ProtectedPayload] IS NULL)");
            });
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Recipient).HasMaxLength(256).IsRequired();
            b.Property(m => m.Subject).HasMaxLength(200).IsRequired();
            b.Property(m => m.ProtectedPayload).HasMaxLength(32768);
            b.Property(m => m.State).HasMaxLength(20).IsRequired();
            b.Property(m => m.LastError).HasMaxLength(100);
            b.HasIndex(m => new { m.State, m.NextAttemptAtUtc, m.LeaseUntilUtc });
            b.HasIndex(m => m.TokenId);
        });
    }
}

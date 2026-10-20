namespace Algara.Data.Models;

/// <summary>Current account membership; an unlinked row is retained for concurrency checks.</summary>
public class PartnerAccountLink
{
    // References Users.N in IdentityDbContext; the database constraint is added in the migration.
    public int UserN { get; set; }
    public int? PartnerN { get; set; }
    public Partner? Partner { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

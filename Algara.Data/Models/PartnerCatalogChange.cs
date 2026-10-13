namespace Algara.Data.Models;

public class PartnerCatalogChange
{
    public long N { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityN { get; set; }
    // The existing authentication identifier, independent of mutable profile data.
    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = string.Empty;
}

namespace Algara.Identity.Models;

public sealed class AccountEmailAuditEvent
{
    public long Id { get; set; }
    public Guid? TokenId { get; set; }
    public int? UserN { get; set; }
    public int? PartnerN { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

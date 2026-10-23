namespace Algara.Identity.Models;

/// <summary>Administrative settings history, with snapshots that exclude credentials.</summary>
public sealed class SmtpSettingsAuditEvent
{
    public long Id { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
}

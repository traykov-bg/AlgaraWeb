namespace Algara.Data.Models;

/// <summary>A bounded staff work context, never a customer authentication session.</summary>
public sealed class CustomerAssistanceSession
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = string.Empty;
    // Numeric UserSessions.Id, never the authentication session key.
    public int ActorSessionN { get; set; }
    public int UserN { get; set; }
    public int Mode { get; set; }
    public string TargetSecurityVersion { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string? EndReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

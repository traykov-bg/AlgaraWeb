namespace Algara.Identity.Models;

/// <summary>A durable delivery attempt. Tokens and message bodies exist only in protected payloads.</summary>
public sealed class EmailOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? ProtectedPayload { get; set; }
    public Guid? TokenId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public string State { get; set; } = EmailOutboxStates.Pending;
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public string? LastError { get; set; }
}

public static class EmailOutboxStates
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Accepted = "Accepted";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

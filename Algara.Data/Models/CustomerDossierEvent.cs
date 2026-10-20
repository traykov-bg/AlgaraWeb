namespace Algara.Data.Models;

/// <summary>Append-only internal notes and account-link history attributed to the actual staff actor.</summary>
public class CustomerDossierEvent
{
    public long N { get; set; }
    // References Users.N without introducing a dependency on the Identity project.
    public int UserN { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? VerificationMethod { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Note { get; set; }
}

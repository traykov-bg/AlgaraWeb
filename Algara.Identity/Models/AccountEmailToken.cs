namespace Algara.Identity.Models;

public enum AccountEmailPurpose { PasswordReset = 1, EmailConfirmation = 2, PartnerInvitation = 3 }

public sealed class AccountEmailToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AccountEmailPurpose Purpose { get; set; }
    public int? UserN { get; set; }
    public int? PartnerN { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string? ExpectedSecurityStamp { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    public string? VerificationMethod { get; set; }
    public string? Reason { get; set; }
    public byte[]? PartnerVersion { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

using System.Security.Claims;
using Algara.Identity.Services;

namespace Algara.Web.Services.Email;

public sealed record AccountEmailResult(bool Success, string? Error = null);

public sealed class PartnerInvitationRequest
{
    public int PartnerN { get; set; }
    public string? ExpectedPartnerVersion { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool ConfirmedSpecificPartner { get; set; }
    public bool ConfirmedCustomerRequest { get; set; }
    public string? VerificationMethod { get; set; }
    public string? Reason { get; set; }
}

public interface IAccountEmailService
{
    Task<AccountEmailResult> RegisterAsync(RegistrationData data, CancellationToken cancellationToken = default);
    Task RequestResetAsync(string email, CancellationToken cancellationToken = default);
    Task RequestConfirmationAsync(string email, CancellationToken cancellationToken = default);
    Task<AccountEmailResult> ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default);
    Task<AccountEmailResult> ConfirmEmailAsync(string token, CancellationToken cancellationToken = default);
    Task<AccountEmailResult> AcceptInvitationAsync(string token, RegistrationData data, CancellationToken cancellationToken = default);
    Task<AccountEmailResult> SendResetAsync(ClaimsPrincipal actor, int userN, string? expectedAccountVersion, string? reason, CancellationToken cancellationToken = default);
    Task<AccountEmailResult> SendConfirmationAsync(ClaimsPrincipal actor, int userN, string? expectedAccountVersion, string? reason, CancellationToken cancellationToken = default);
    Task<AccountEmailResult> InviteAsync(ClaimsPrincipal actor, PartnerInvitationRequest request, CancellationToken cancellationToken = default);
    Task<AccountEmailResult> ResendInvitationAsync(ClaimsPrincipal actor, Guid id, string? expectedVersion, CancellationToken cancellationToken = default);
    Task<AccountEmailResult> CancelInvitationAsync(ClaimsPrincipal actor, Guid id, string? expectedVersion, string? reason, CancellationToken cancellationToken = default);
}

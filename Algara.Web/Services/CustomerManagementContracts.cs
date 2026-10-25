using System.Security.Claims;

namespace Algara.Web.Services;

public class CustomerAccountAction
{
    public string? ExpectedAccountVersion { get; set; }
    public string? Reason { get; set; }
}

public sealed class CustomerProfileUpdate : CustomerAccountAction
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

public sealed class CustomerAddressUpdate : CustomerAccountAction
{
    public int? N { get; set; }
    public string? ExpectedAddressesVersion { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Email { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public string Country { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class CustomerAddressAction : CustomerAccountAction
{
    public int AddressN { get; set; }
    public string? ExpectedAddressesVersion { get; set; }
}

public sealed class CustomerSessionAction : CustomerAccountAction
{
    // The numeric database row identifier is not the authentication session key.
    public int SessionN { get; set; }
    public string? ExpectedSessionVersion { get; set; }
}

public sealed record CustomerSessionSummary(int Id, DateTime CreatedAt, string DeviceInfo,
    bool IsActive, bool IsCurrent, string Version);

public sealed record CustomerSessionsSnapshot(string AccountVersion, IReadOnlyList<CustomerSessionSummary> Sessions);

public interface ICustomerManagementService
{
    Task<CustomerDossierResult> EditProfileAsync(ClaimsPrincipal actor, int userN, CustomerProfileUpdate request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> EditOwnProfileAsync(ClaimsPrincipal actor, CustomerProfileUpdate request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> SaveAddressAsync(ClaimsPrincipal actor, int userN, CustomerAddressUpdate request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> SaveOwnAddressAsync(ClaimsPrincipal actor, CustomerAddressUpdate request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> DeleteAddressAsync(ClaimsPrincipal actor, int userN, CustomerAddressAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> DeleteOwnAddressAsync(ClaimsPrincipal actor, CustomerAddressAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> SetDefaultAddressAsync(ClaimsPrincipal actor, int userN, CustomerAddressAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> SetDefaultOwnAddressAsync(ClaimsPrincipal actor, CustomerAddressAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> UnlockAsync(ClaimsPrincipal actor, int userN, CustomerAccountAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> SuspendAccessAsync(ClaimsPrincipal actor, int userN, CustomerAccountAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> RestoreAccessAsync(ClaimsPrincipal actor, int userN, CustomerAccountAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> RevokeSessionAsync(ClaimsPrincipal actor, int userN, CustomerSessionAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> RevokeAllSessionsAsync(ClaimsPrincipal actor, int userN, CustomerAccountAction request, CancellationToken cancellationToken = default);
    Task<CustomerSessionsSnapshot?> GetSessionsAsync(ClaimsPrincipal actor, int userN, CancellationToken cancellationToken = default);
}

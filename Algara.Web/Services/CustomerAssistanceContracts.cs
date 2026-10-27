using System.Security.Claims;
using Algara.Web.ViewModels;

namespace Algara.Web.Services;

public enum CustomerAssistanceMode { Preview = 1, Assist = 2 }

public sealed record CustomerAssistanceStartResult(bool Success, Guid? SessionId = null, string? Error = null);

public sealed class CustomerAssistanceSnapshot
{
    public Guid SessionId { get; init; }
    public int UserN { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public CustomerAssistanceMode Mode { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool CanEditProfile { get; init; }
    public bool CanManageAddresses { get; init; }
    public bool CanViewOrders { get; init; }
    public ProfileViewModel Profile { get; init; } = new();
}

public sealed record CustomerAssistanceOrderSnapshot(CustomerAssistanceSnapshot Context, ProfileOrderDetailViewModel Details);

public interface ICustomerAssistanceService
{
    Task<CustomerAssistanceStartResult> StartAsync(ClaimsPrincipal actor, int userN, CustomerAssistanceMode mode,
        string? expectedAccountVersion, string? reason, CancellationToken cancellationToken = default);
    Task<CustomerAssistanceSnapshot?> GetAsync(ClaimsPrincipal actor, Guid sessionId, CancellationToken cancellationToken = default);
    Task<CustomerAssistanceOrderSnapshot?> GetOrderAsync(ClaimsPrincipal actor, Guid sessionId, int orderN, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> EndAsync(ClaimsPrincipal actor, Guid sessionId, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> RecordValidationFailureAsync(ClaimsPrincipal actor, Guid sessionId, string operation, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> EditProfileAsync(ClaimsPrincipal actor, Guid sessionId, CustomerProfileUpdate request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> SaveAddressAsync(ClaimsPrincipal actor, Guid sessionId, CustomerAddressUpdate request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> DeleteAddressAsync(ClaimsPrincipal actor, Guid sessionId, CustomerAddressAction request, CancellationToken cancellationToken = default);
    Task<CustomerDossierResult> SetDefaultAddressAsync(ClaimsPrincipal actor, Guid sessionId, CustomerAddressAction request, CancellationToken cancellationToken = default);
}

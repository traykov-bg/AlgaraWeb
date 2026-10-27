using System.ComponentModel.DataAnnotations;
using Algara.Web.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Algara.Web.ViewModels;

public sealed class CustomerAssistanceStartForm
{
    [BindRequired, Required]
    public string? ExpectedAccountVersion { get; set; }

    [Required(ErrorMessage = "Запишете основание или номер на клиентското запитване."), StringLength(2000)]
    public string? Reason { get; set; }

    [Range(1, 2)]
    public CustomerAssistanceMode Mode { get; set; }
}

/// <summary>Display context only. Authorization and target selection remain in the service.</summary>
public sealed class CustomerAssistanceViewModel
{
    public Guid SessionId { get; init; }
    public int UserN { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public CustomerAssistanceMode Mode { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool CanEditProfile { get; init; }
    public bool CanManageAddresses { get; init; }
    public bool CanViewOrders { get; init; }
}

public sealed class CustomerAssistanceStartErrorViewModel
{
    public int UserN { get; init; }
    public string Error { get; init; } = string.Empty;
}

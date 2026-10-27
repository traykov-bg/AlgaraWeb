using Algara.Web.Services;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Algara.Web.Controllers;

// The service checks current permissions under the shared access lock. Requests must
// reach it after a grant is revoked so owned contexts can be closed and audited.
[Authorize]
[Route("customer-assistance")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerAssistanceController(ICustomerAssistanceService assistance) : Controller
{
    [NonAction]
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        base.OnActionExecuting(context);
    }

    [HttpPost("start/{n:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Start([FromRoute] int n, CustomerAssistanceStartForm form)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("StartError", new CustomerAssistanceStartErrorViewModel
            {
                UserN = n, Error = "Изберете режим, попълнете основанието и започнете от актуалното досие."
            });
        }
        var result = await assistance.StartAsync(User, n, form.Mode, form.ExpectedAccountVersion,
            form.Reason, HttpContext.RequestAborted);
        if (result.Success && result.SessionId.HasValue)
            return RedirectToAction(nameof(Profile), new { sessionId = result.SessionId.Value });
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("StartError", new CustomerAssistanceStartErrorViewModel
        {
            UserN = n, Error = result.Error ?? "Прегледът или съдействието не е започнато. Презаредете досието."
        });
    }

    [HttpGet("{sessionId:guid}")]
    public async Task<IActionResult> Profile([FromRoute] Guid sessionId, string? tab = null) =>
        await ProfileResultAsync(sessionId, tab);

    [HttpGet("{sessionId:guid}/orders/{n:int}")]
    public async Task<IActionResult> OrderDetails([FromRoute] Guid sessionId, [FromRoute] int n)
    {
        var snapshot = await assistance.GetOrderAsync(User, sessionId, n, HttpContext.RequestAborted);
        if (snapshot == null) return Unavailable();
        var context = ToViewModel(snapshot.Context);
        snapshot.Details.Assistance = context;
        ViewData["Assistance"] = context;
        return View("~/Views/Account/OrderDetails.cshtml", snapshot.Details);
    }

    [HttpPost("{sessionId:guid}/profile"), ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile([FromRoute] Guid sessionId,
        [Bind(Prefix = "Details")] ProfileDetailsViewModel details)
    {
        if (!ModelState.IsValid)
        {
            await RecordValidationAsync(sessionId, "ProfileUpdated");
            return await ProfileResultAsync(sessionId, "details", details: details);
        }
        var result = await assistance.EditProfileAsync(User, sessionId, new CustomerProfileUpdate
        {
            ExpectedAccountVersion = details.ExpectedVersion,
            FirstName = details.FirstName, LastName = details.LastName, PhoneNumber = details.PhoneNumber
        }, HttpContext.RequestAborted);
        if (result.Success)
        {
            ModelState.Clear();
            return await ProfileResultAsync(sessionId, "details", status: "Профилните данни са записани с Вашето име в служебната история.");
        }
        ModelState.AddModelError(string.Empty, result.Error ?? "Промяната не е записана.");
        return await ProfileResultAsync(sessionId, "details", details: details);
    }

    [HttpPost("{sessionId:guid}/addresses"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAddress([FromRoute] Guid sessionId,
        [Bind(Prefix = "Address")] AddressFormViewModel address)
    {
        if (!ModelState.IsValid)
        {
            await RecordValidationAsync(sessionId, address.N.HasValue ? "AddressUpdated" : "AddressCreated");
            return await ProfileResultAsync(sessionId, "addresses", address: address);
        }
        var result = await assistance.SaveAddressAsync(User, sessionId, new CustomerAddressUpdate
        {
            N = address.N, ExpectedAccountVersion = address.ExpectedAccountVersion,
            ExpectedAddressesVersion = address.ExpectedVersion, FirstName = address.FirstName,
            LastName = address.LastName, PhoneNumber = address.PhoneNumber, Email = address.Email,
            AddressLine1 = address.AddressLine1, AddressLine2 = address.AddressLine2, City = address.City,
            PostalCode = address.PostalCode, Country = address.Country, IsDefault = address.IsDefault
        }, HttpContext.RequestAborted);
        if (result.Success)
        {
            ModelState.Clear();
            return await ProfileResultAsync(sessionId, "addresses", status: "Адресът е записан с Вашето име в служебната история.");
        }
        ModelState.AddModelError(string.Empty, result.Error ?? "Адресът не е записан.");
        return await ProfileResultAsync(sessionId, "addresses", address: address);
    }

    [HttpPost("{sessionId:guid}/addresses/delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAddress([FromRoute] Guid sessionId, int n,
        string? expectedVersion, string? expectedAccountVersion) =>
        await AddressActionAsync(sessionId, n, expectedVersion, expectedAccountVersion, false);

    [HttpPost("{sessionId:guid}/addresses/default"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultAddress([FromRoute] Guid sessionId, int n,
        string? expectedVersion, string? expectedAccountVersion) =>
        await AddressActionAsync(sessionId, n, expectedVersion, expectedAccountVersion, true);

    [HttpPost("{sessionId:guid}/end"), ValidateAntiForgeryToken]
    public async Task<IActionResult> End([FromRoute] Guid sessionId)
    {
        var snapshot = await assistance.GetAsync(User, sessionId, HttpContext.RequestAborted);
        var result = await assistance.EndAsync(User, sessionId, HttpContext.RequestAborted);
        if (!result.Success) return Unavailable();
        return snapshot == null
            ? RedirectToAction("Users", "Admin")
            : RedirectToAction("Dossier", "AdminCustomers", new { n = snapshot.UserN });
    }

    private async Task<IActionResult> AddressActionAsync(Guid sessionId, int n, string? expectedVersion,
        string? expectedAccountVersion, bool setDefault)
    {
        if (!ModelState.IsValid)
        {
            await RecordValidationAsync(sessionId, setDefault ? "DefaultAddressChanged" : "AddressDeleted");
            return await ProfileResultAsync(sessionId, "addresses");
        }
        var request = new CustomerAddressAction
        {
            AddressN = n, ExpectedAddressesVersion = expectedVersion, ExpectedAccountVersion = expectedAccountVersion
        };
        var result = setDefault
            ? await assistance.SetDefaultAddressAsync(User, sessionId, request, HttpContext.RequestAborted)
            : await assistance.DeleteAddressAsync(User, sessionId, request, HttpContext.RequestAborted);
        ModelState.Clear();
        if (!result.Success) ModelState.AddModelError(string.Empty, result.Error ?? "Адресите не са променени.");
        return await ProfileResultAsync(sessionId, "addresses", status: result.Success
            ? setDefault ? "Основният адрес е променен." : "Адресът е изтрит."
            : null);
    }

    private async Task<IActionResult> ProfileResultAsync(Guid sessionId, string? tab,
        ProfileDetailsViewModel? details = null, AddressFormViewModel? address = null, string? status = null)
    {
        var snapshot = await assistance.GetAsync(User, sessionId, HttpContext.RequestAborted);
        if (snapshot == null) return Unavailable();
        var profile = snapshot.Profile;
        var context = ToViewModel(snapshot);
        profile.Assistance = context;
        profile.ActiveTab = tab is "addresses" or "details" ? tab
            : snapshot.CanViewOrders && tab == "history" ? "history"
            : snapshot.CanViewOrders ? "current" : "details";
        if (details != null) { details.Email = profile.Details.Email; profile.Details = details; }
        if (address != null) { profile.Address = address; profile.ShowAddressModal = snapshot.CanManageAddresses; }
        profile.StatusMessage = status;
        ViewData["Assistance"] = context;
        return View("~/Views/Account/Profile.cshtml", profile);
    }

    private ViewResult Unavailable()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("Unavailable");
    }

    private async Task RecordValidationAsync(Guid sessionId, string operation)
    {
        var result = await assistance.RecordValidationFailureAsync(User, sessionId, operation, HttpContext.RequestAborted);
        if (!result.Success) ModelState.AddModelError(string.Empty, result.Error ?? "Действието не може да бъде обработено.");
    }

    private static CustomerAssistanceViewModel ToViewModel(CustomerAssistanceSnapshot snapshot) => new()
    {
        SessionId = snapshot.SessionId, UserN = snapshot.UserN, CustomerName = snapshot.CustomerName,
        Mode = snapshot.Mode, ExpiresAtUtc = snapshot.ExpiresAtUtc, Reason = snapshot.Reason,
        CanEditProfile = snapshot.CanEditProfile, CanManageAddresses = snapshot.CanManageAddresses,
        CanViewOrders = snapshot.CanViewOrders
    };
}

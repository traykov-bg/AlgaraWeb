using System.Security.Claims;
using Algara.Web.Controllers;
using Algara.Web.Services;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Algara.UnitTests.Controllers;

public sealed class CustomerAssistanceControllerTests
{
    private readonly Mock<ICustomerAssistanceService> _service = new(MockBehavior.Strict);
    private readonly CustomerAssistanceController _controller;
    private readonly Guid _session = Guid.NewGuid();
    private readonly ClaimsPrincipal _actor = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actual-staff")], "test"));

    public CustomerAssistanceControllerTests()
    {
        _controller = new(_service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = _actor } }
        };
    }

    [Fact]
    public async Task InvalidStartDoesNotCreateAContext()
    {
        _controller.ModelState.AddModelError("Reason", "Required");
        var result = Assert.IsType<ViewResult>(await _controller.Start(15, new()));
        Assert.Equal("StartError", result.ViewName);
        Assert.Equal(400, _controller.Response.StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StartUsesActualActorExactTargetVersionAndRecordedReason()
    {
        var form = new CustomerAssistanceStartForm { Mode = CustomerAssistanceMode.Preview, ExpectedAccountVersion = "exact-version", Reason = "Customer support case" };
        _service.Setup(s => s.StartAsync(_actor, 15, form.Mode, form.ExpectedAccountVersion, form.Reason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerAssistanceStartResult(true, _session));
        var redirect = Assert.IsType<RedirectToActionResult>(await _controller.Start(15, form));
        Assert.Equal(nameof(CustomerAssistanceController.Profile), redirect.ActionName);
        Assert.Equal(_session, redirect.RouteValues!["sessionId"]);
        Assert.Same(_actor, _controller.User);
    }

    [Fact]
    public async Task UnavailableContextReturnsGenericDenialWithoutSigningOutTheStaffActor()
    {
        _service.Setup(s => s.GetAsync(_actor, _session, It.IsAny<CancellationToken>())).ReturnsAsync((CustomerAssistanceSnapshot?)null);
        var result = Assert.IsType<ViewResult>(await _controller.Profile(_session));
        Assert.Equal("Unavailable", result.ViewName);
        Assert.Equal(403, _controller.Response.StatusCode);
        Assert.Same(_actor, _controller.User);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("privacy")]
    [InlineData("security")]
    [InlineData("unknown")]
    public async Task UnsupportedTabsCannotSelectSecurityOrConsentPanels(string tab)
    {
        SetSnapshot(canViewOrders: false);
        var model = Assert.IsType<ProfileViewModel>(Assert.IsType<ViewResult>(await _controller.Profile(_session, tab)).Model);
        Assert.Equal("details", model.ActiveTab);
        Assert.Equal(_session, model.Assistance!.SessionId);
        Assert.Equal(15, model.Assistance.UserN);
    }

    [Fact]
    public async Task ProfileForwardsOnlyAllowedContactFieldsAndSessionIdentifier()
    {
        var details = new ProfileDetailsViewModel { ExpectedVersion = "v1", FirstName = "First", LastName = "Last", PhoneNumber = "+359888123456", Email = "must-not-change@example.test", AddressLine1 = "must-not-edit" };
        CustomerProfileUpdate? forwarded = null;
        _service.Setup(s => s.EditProfileAsync(_actor, _session, It.IsAny<CustomerProfileUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<ClaimsPrincipal, Guid, CustomerProfileUpdate, CancellationToken>((_, _, request, _) => forwarded = request)
            .ReturnsAsync(new CustomerDossierResult(true));
        SetSnapshot();
        await _controller.UpdateProfile(_session, details);
        Assert.NotNull(forwarded);
        Assert.Equal("v1", forwarded.ExpectedAccountVersion);
        Assert.Equal("First", forwarded.FirstName);
        Assert.Equal("Last", forwarded.LastName);
        Assert.Equal("+359888123456", forwarded.PhoneNumber);
        Assert.Null(forwarded.Reason);
        Assert.Same(_actor, _controller.User);
    }

    [Fact]
    public async Task FailedProfileEditPreservesPostedVersionButRestoresAuthoritativeLoginEmail()
    {
        var details = new ProfileDetailsViewModel { ExpectedVersion = "stale-version", FirstName = "Unsaved", LastName = "Customer", Email = "forged@example.test" };
        _service.Setup(s => s.EditProfileAsync(_actor, _session, It.IsAny<CustomerProfileUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerDossierResult(false, "Stale"));
        SetSnapshot();
        var model = Assert.IsType<ProfileViewModel>(Assert.IsType<ViewResult>(await _controller.UpdateProfile(_session, details)).Model);
        Assert.Equal("stale-version", model.Details.ExpectedVersion);
        Assert.Equal("Unsaved", model.Details.FirstName);
        Assert.Equal("selected@example.test", model.Details.Email);
        Assert.False(_controller.ModelState.IsValid);
    }

    [Fact]
    public async Task InvalidProfileDoesNotInvokeMutationService()
    {
        _controller.ModelState.AddModelError("Details.FirstName", "Required");
        _service.Setup(s => s.RecordValidationFailureAsync(_actor, _session, "ProfileUpdated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerDossierResult(true));
        SetSnapshot();
        await _controller.UpdateProfile(_session, new ProfileDetailsViewModel());
        _service.Verify(s => s.EditProfileAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>(), It.IsAny<CustomerProfileUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
        _service.Verify(s => s.RecordValidationFailureAsync(_actor, _session, "ProfileUpdated", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddressSaveForwardsExactAddressAndAccountVersions()
    {
        CustomerAddressUpdate? forwarded = null;
        _service.Setup(s => s.SaveAddressAsync(_actor, _session, It.IsAny<CustomerAddressUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<ClaimsPrincipal, Guid, CustomerAddressUpdate, CancellationToken>((_, _, request, _) => forwarded = request)
            .ReturnsAsync(new CustomerDossierResult(true));
        SetSnapshot();
        await _controller.SaveAddress(_session, new AddressFormViewModel { N = 27, ExpectedVersion = "book-v1", ExpectedAccountVersion = "account-v1", FirstName = "Recipient", LastName = "Name", Email = "delivery@example.test", AddressLine1 = "Street 1", City = "Sofia", Country = "Bulgaria", IsDefault = true });
        Assert.NotNull(forwarded);
        Assert.Equal(27, forwarded.N);
        Assert.Equal("book-v1", forwarded.ExpectedAddressesVersion);
        Assert.Equal("account-v1", forwarded.ExpectedAccountVersion);
        Assert.Equal("delivery@example.test", forwarded.Email);
        Assert.True(forwarded.IsDefault);
    }

    [Theory]
    [InlineData(null, "AddressCreated")]
    [InlineData(27, "AddressUpdated")]
    public async Task InvalidAddressBindingRecordsFailureWithoutCallingAddressMutation(int? addressN, string operation)
    {
        _controller.ModelState.AddModelError("Address.N", "Malformed address identifier");
        _service.Setup(s => s.RecordValidationFailureAsync(_actor, _session, operation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerDossierResult(true));
        SetSnapshot();
        var result = Assert.IsType<ViewResult>(await _controller.SaveAddress(_session, new AddressFormViewModel { N = addressN, ExpectedVersion = "original-book" }));
        var model = Assert.IsType<ProfileViewModel>(result.Model);
        Assert.Equal("original-book", model.Address.ExpectedVersion);
        Assert.True(model.ShowAddressModal);
        _service.Verify(s => s.SaveAddressAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>(), It.IsAny<CustomerAddressUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
        _service.Verify(s => s.RecordValidationFailureAsync(_actor, _session, operation, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddressActionsRetainContextAndBothOptimisticVersions(bool setDefault)
    {
        CustomerAddressAction? forwarded = null;
        if (setDefault)
            _service.Setup(s => s.SetDefaultAddressAsync(_actor, _session, It.IsAny<CustomerAddressAction>(), It.IsAny<CancellationToken>()))
                .Callback<ClaimsPrincipal, Guid, CustomerAddressAction, CancellationToken>((_, _, r, _) => forwarded = r).ReturnsAsync(new CustomerDossierResult(true));
        else
            _service.Setup(s => s.DeleteAddressAsync(_actor, _session, It.IsAny<CustomerAddressAction>(), It.IsAny<CancellationToken>()))
                .Callback<ClaimsPrincipal, Guid, CustomerAddressAction, CancellationToken>((_, _, r, _) => forwarded = r).ReturnsAsync(new CustomerDossierResult(true));
        SetSnapshot();
        if (setDefault) await _controller.SetDefaultAddress(_session, 27, "book-version", "account-version");
        else await _controller.DeleteAddress(_session, 27, "book-version", "account-version");
        Assert.NotNull(forwarded);
        Assert.Equal(27, forwarded.AddressN);
        Assert.Equal("book-version", forwarded.ExpectedAddressesVersion);
        Assert.Equal("account-version", forwarded.ExpectedAccountVersion);
    }

    [Fact]
    public async Task EndReturnsToSelectedCustomerDossier()
    {
        SetSnapshot();
        _service.Setup(s => s.EndAsync(_actor, _session, It.IsAny<CancellationToken>())).ReturnsAsync(new CustomerDossierResult(true));
        var result = Assert.IsType<RedirectToActionResult>(await _controller.End(_session));
        Assert.Equal("Dossier", result.ActionName);
        Assert.Equal("AdminCustomers", result.ControllerName);
        Assert.Equal(15, result.RouteValues!["n"]);
    }

    private void SetSnapshot(bool canViewOrders = true)
    {
        _service.Setup(s => s.GetAsync(_actor, _session, It.IsAny<CancellationToken>())).ReturnsAsync(() => new CustomerAssistanceSnapshot
        {
            SessionId = _session, UserN = 15, CustomerName = "Selected Customer", Mode = CustomerAssistanceMode.Assist,
            CanEditProfile = true, CanManageAddresses = true, CanViewOrders = canViewOrders,
            StartedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            Profile = new ProfileViewModel { Details = new ProfileDetailsViewModel { Email = "selected@example.test" } }
        });
    }
}

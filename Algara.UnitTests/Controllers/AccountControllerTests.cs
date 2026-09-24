using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Algara.Data.Repositories;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Algara.Web.Controllers;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Algara.UnitTests.Controllers;

// Direct action tests: MVC model validation, authorization and antiforgery filters
// run in the HTTP pipeline and are outside the scope of this suite.
public sealed class AccountControllerTests : IDisposable
{
    private readonly Mock<IUserService> _users = new(MockBehavior.Strict);
    private readonly Mock<IOrderRepository> _orders = new(MockBehavior.Strict);
    private readonly IdentityDbContext _identityDb = new(new DbContextOptions<IdentityDbContext>());
    private readonly DefaultHttpContext _http = new();
    private readonly AccountController _controller;

    public AccountControllerTests()
    {
        _controller = new AccountController(_users.Object, _identityDb, _orders.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = _http, RouteData = new RouteData() }
        };
        _controller.Url = new HomeUrlHelper(_controller.ControllerContext);
    }

    [Fact]
    public async Task Register_InvalidModel_ReturnsSubmittedModelWithoutCreatingAccount()
    {
        var model = NewRegistration();
        _controller.ModelState.AddModelError(nameof(model.TermsAccepted), "Terms are required.");

        var result = await _controller.Register(model);

        Assert.Same(model, Assert.IsType<ViewResult>(result).Model);
        _users.VerifyNoOtherCalls();
        _orders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsErrorWithoutAssigningRoleOrSigningIn()
    {
        var model = NewRegistration();
        _users.Setup(s => s.RegisterUserAsync(It.Is<RegistrationData>(d => d.Email == model.Email)))
            .ReturnsAsync((ApplicationUser?)null);

        var result = await _controller.Register(model);

        Assert.Same(model, Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Имейлът вече е регистриран.", SingleModelError());
        _users.Verify(s => s.RegisterUserAsync(It.Is<RegistrationData>(d => d.Email == model.Email)), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_ValidModel_PassesConsentAndRequestAuditThenSignsInAsCustomer()
    {
        var model = NewRegistration();
        model.FirstName = "  Анна  ";
        model.LastName = "  Иванова  ";
        model.MarketingConsent = true;
        _http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.15");
        _http.Request.Headers.UserAgent = "Account unit test";
        var user = NewUser();
        RegistrationData? captured = null;
        _users.Setup(s => s.RegisterUserAsync(It.IsAny<RegistrationData>()))
            .Callback<RegistrationData>(data => captured = data)
            .ReturnsAsync(user);
        _users.Setup(s => s.AddUserToRoleAsync(user.UserName, "User")).ReturnsAsync(true);
        _users.Setup(s => s.SignInAsync(_http, user, false, null)).Returns(Task.CompletedTask);

        var result = await _controller.Register(model);

        AssertHomeRedirect(result);
        Assert.NotNull(captured);
        Assert.Equal(model.Email, captured.Email);
        Assert.Equal(model.Password, captured.Password);
        Assert.Equal("Анна", captured.FirstName);
        Assert.Equal("Иванова", captured.LastName);
        Assert.Equal(model.PhoneNumber, captured.PhoneNumber);
        Assert.True(captured.MarketingConsent);
        Assert.Equal("192.0.2.15", captured.IpAddress);
        Assert.Equal("Account unit test", captured.UserAgent);
        _users.Verify(s => s.RegisterUserAsync(It.IsAny<RegistrationData>()), Times.Once);
        _users.Verify(s => s.AddUserToRoleAsync(user.UserName, "User"), Times.Once);
        _users.Verify(s => s.SignInAsync(_http, user, false, null), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_InvalidModel_ReturnsErrorsWithoutLookingUpOrSigningInUser(bool ajax)
    {
        var model = NewLogin();
        SetAjax(ajax);
        _controller.ModelState.AddModelError(nameof(model.Email), "Invalid email.");

        var result = await _controller.Login(model, "/Account/Profile");

        AssertLoginFailure(result, model, ajax, "Invalid email.", nameof(model.Email));
        _users.VerifyNoOtherCalls();
        _orders.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Login_UnknownUserOrWrongPassword_ReturnsSameGenericErrorWithoutSigningIn(
        bool userExists, bool ajax)
    {
        var model = NewLogin();
        SetAjax(ajax);
        _users.Setup(s => s.GetUserByUsernameAsync(model.Email))
            .ReturnsAsync(userExists ? NewUser() : null);
        if (userExists)
            _users.Setup(s => s.ValidateUserAsync(model.Email, model.Password)).ReturnsAsync(false);

        var result = await _controller.Login(model, "/Account/Profile");

        AssertLoginFailure(result, model, ajax, "Грешен имейл или парола.", string.Empty);
        _users.Verify(s => s.GetUserByUsernameAsync(model.Email), Times.Once);
        if (userExists)
            _users.Verify(s => s.ValidateUserAsync(model.Email, model.Password), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Login_LockedUser_DoesNotValidatePasswordOrSignIn()
    {
        var model = NewLogin();
        var user = NewUser();
        user.LockoutUntil = DateTime.Now.AddDays(1);
        _users.Setup(s => s.GetUserByUsernameAsync(model.Email)).ReturnsAsync(user);

        var result = await _controller.Login(model);

        Assert.Same(model, Assert.IsType<ViewResult>(result).Model);
        Assert.Contains("заключен", SingleModelError());
        _users.Verify(s => s.GetUserByUsernameAsync(model.Email), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, 180)]
    public async Task Login_ValidCredentials_ForwardsRememberMeAndTimeZone(bool rememberMe, int? offset)
    {
        var model = NewLogin();
        model.RememberMe = rememberMe;
        model.TimeZoneOffset = offset;
        var user = ArrangeSuccessfulLogin(model);

        var result = await _controller.Login(model, "/Account/Profile");

        Assert.Equal("/Account/Profile", Assert.IsType<RedirectResult>(result).Url);
        VerifySuccessfulLogin(model, user);
    }

    [Theory]
    [InlineData("/Account/Profile", false, "/Account/Profile")]
    [InlineData("/Account/Profile", true, "/Account/Profile")]
    [InlineData("~/Account/Profile", false, "~/Account/Profile")]
    [InlineData("https://untrusted.example/path", false, null)]
    [InlineData("https://untrusted.example/path", true, null)]
    [InlineData("//untrusted.example/path", false, null)]
    [InlineData("//untrusted.example/path", true, null)]
    [InlineData("/\\untrusted.example/path", false, null)]
    [InlineData("/\\untrusted.example/path", true, null)]
    [InlineData(null, false, null)]
    [InlineData(null, true, null)]
    public async Task Login_ValidCredentials_UsesOnlyLocalReturnUrl(
        string? returnUrl, bool ajax, string? expectedLocalUrl)
    {
        var model = NewLogin();
        SetAjax(ajax);
        var user = ArrangeSuccessfulLogin(model);

        var result = await _controller.Login(model, returnUrl);

        if (ajax)
        {
            var payload = JsonPayload(result);
            Assert.True(payload.GetProperty("success").GetBoolean());
            Assert.Equal(expectedLocalUrl ?? "/", payload.GetProperty("redirectUrl").GetString());
        }
        else if (expectedLocalUrl is not null)
        {
            Assert.Equal(expectedLocalUrl, Assert.IsType<RedirectResult>(result).Url);
        }
        else
        {
            AssertHomeRedirect(result);
        }
        VerifySuccessfulLogin(model, user);
    }

    [Theory]
    [InlineData("https://untrusted.example/path")]
    [InlineData("//untrusted.example/path")]
    public void Login_AlreadyAuthenticated_RejectsExternalReturnUrl(string returnUrl)
    {
        _http.User = new ClaimsPrincipal(new ClaimsIdentity("UnitTest"));

        var result = _controller.Login(returnUrl);

        AssertHomeRedirect(result);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Logout_EndsCurrentSessionAndReturnsHome()
    {
        _users.Setup(s => s.SignOutAsync(_http)).Returns(Task.CompletedTask);

        var result = await _controller.Logout();

        AssertHomeRedirect(result);
        _users.Verify(s => s.SignOutAsync(_http), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task OrderDetails_MissingUserIdentifier_ReturnsToLoginWithoutReadingOrders(string? userId)
    {
        Claim[] claims = userId is null ? [] : [new Claim(ClaimTypes.NameIdentifier, userId)];
        _http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "UnitTest"));

        var result = await _controller.OrderDetails(42);

        Assert.Equal("Login", Assert.IsType<RedirectToActionResult>(result).ActionName);
        _users.VerifyNoOtherCalls();
        _orders.VerifyNoOtherCalls();
    }

    private ApplicationUser ArrangeSuccessfulLogin(LoginViewModel model)
    {
        var user = NewUser();
        _users.Setup(s => s.GetUserByUsernameAsync(model.Email)).ReturnsAsync(user);
        _users.Setup(s => s.ValidateUserAsync(model.Email, model.Password)).ReturnsAsync(true);
        _users.Setup(s => s.SignInAsync(_http, user, model.RememberMe, model.TimeZoneOffset))
            .Returns(Task.CompletedTask);
        return user;
    }

    private void VerifySuccessfulLogin(LoginViewModel model, ApplicationUser user)
    {
        _users.Verify(s => s.GetUserByUsernameAsync(model.Email), Times.Once);
        _users.Verify(s => s.ValidateUserAsync(model.Email, model.Password), Times.Once);
        _users.Verify(s => s.SignInAsync(_http, user, model.RememberMe, model.TimeZoneOffset), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    private void AssertLoginFailure(IActionResult result, LoginViewModel model, bool ajax, string error, string key)
    {
        Assert.Equal(error, SingleModelError());
        if (ajax)
        {
            var payload = JsonPayload(result);
            Assert.False(payload.GetProperty("success").GetBoolean());
            var errors = payload.GetProperty("errors");
            Assert.Single(errors.EnumerateObject());
            Assert.Equal(error, Assert.Single(errors.GetProperty(key).EnumerateArray()).GetString());
            Assert.False(payload.TryGetProperty("redirectUrl", out _));
        }
        else
        {
            Assert.Same(model, Assert.IsType<ViewResult>(result).Model);
            Assert.Equal("/Account/Profile", _controller.ViewData["ReturnUrl"]);
        }
    }

    private string SingleModelError() => Assert.Single(_controller.ModelState.Values.SelectMany(v => v.Errors)).ErrorMessage;

    private void SetAjax(bool ajax)
    {
        if (ajax)
            _http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
    }

    private static JsonElement JsonPayload(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);

    private static void AssertHomeRedirect(IActionResult result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    private static LoginViewModel NewLogin() => new()
    {
        Email = "customer@example.test",
        Password = "test-password"
    };

    private static RegisterViewModel NewRegistration() => new()
    {
        FirstName = "Анна",
        LastName = "Иванова",
        Email = "customer@example.test",
        Password = "test-password",
        ConfirmPassword = "test-password",
        PhoneNumber = "+359888123456",
        AgeConfirmed = true,
        TermsAccepted = true
    };

    private static ApplicationUser NewUser() => new()
    {
        N = 7,
        UserName = "customer@example.test",
        Email = "customer@example.test"
    };

    // Keep the framework's real IsLocalUrl implementation; only route generation
    // is replaced because these tests do not start MVC's routing pipeline.
    private sealed class HomeUrlHelper(ActionContext context) : UrlHelper(context)
    {
        public override string? Action(UrlActionContext actionContext)
        {
            Assert.Equal("Index", actionContext.Action);
            Assert.Equal("Home", actionContext.Controller);
            return "/";
        }
    }

    public void Dispose() => _identityDb.Dispose();
}

using Algara.Identity.Services;
using Algara.Web.Controllers;
using Algara.Web.Services.Email;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;

namespace Algara.UnitTests.Controllers;

public sealed class AccountEmailControllerTests
{
    private readonly Mock<IAccountEmailService> _email = new(MockBehavior.Strict);
    private readonly AccountEmailController _controller;
    private const string Token = "0123456789abcdef0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public AccountEmailControllerTests() => _controller = new AccountEmailController(_email.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public void TokenPageGets_DoNotReadTokensOrMutateAccounts()
    {
        Assert.Empty(Assert.IsType<AccountEmailTokenForm>(Assert.IsType<ViewResult>(_controller.ConfirmEmail()).Model).Token);
        Assert.Empty(Assert.IsType<ResetPasswordForm>(Assert.IsType<ViewResult>(_controller.ResetPassword()).Model).Token);
        Assert.Empty(Assert.IsType<AcceptInvitationForm>(Assert.IsType<ViewResult>(_controller.AcceptInvitation()).Model).Token);
        _email.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicRequests_RedirectToTheSameGenericNotice(bool confirmation)
    {
        var form = new AccountEmailRequestForm { Email = "synthetic@example.test" };
        if (confirmation) _email.Setup(s => s.RequestConfirmationAsync(form.Email, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        else _email.Setup(s => s.RequestResetAsync(form.Email, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var result = confirmation ? await _controller.ResendConfirmation(form) : await _controller.ForgotPassword(form);
        Assert.Equal("RequestAccepted", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidRequests_DoNotIssueEmail(bool confirmation)
    {
        _controller.ModelState.AddModelError("Email", "Invalid email");
        var form = new AccountEmailRequestForm();
        var result = confirmation ? await _controller.ResendConfirmation(form) : await _controller.ForgotPassword(form);
        Assert.Same(form, Assert.IsType<ViewResult>(result).Model);
        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResetFailure_ClearsPasswordsFromModelAndModelStateWhileRetainingToken()
    {
        var form = new ResetPasswordForm { Token = Token, Password = "synthetic-secret", ConfirmPassword = "different" };
        _controller.ModelState.SetModelValue("Password", new ValueProviderResult(form.Password));
        _controller.ModelState.SetModelValue("ConfirmPassword", new ValueProviderResult(form.ConfirmPassword));
        _controller.ModelState.AddModelError("ConfirmPassword", "Mismatch");
        var result = await _controller.ResetPassword(form);
        Assert.Same(form, Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(Token, form.Token);
        Assert.Empty(form.Password);
        Assert.Empty(form.ConfirmPassword);
        Assert.Null(_controller.ModelState["Password"]!.RawValue);
        Assert.Null(_controller.ModelState["Password"]!.AttemptedValue);
        Assert.Null(_controller.ModelState["ConfirmPassword"]!.RawValue);
        Assert.Single(_controller.ModelState["ConfirmPassword"]!.Errors);
        _email.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvitationAcceptance_ForwardsOnlyClientConsentsAndNoPostedEmail(bool marketing)
    {
        var form = new AcceptInvitationForm
        {
            Token = Token, Password = "synthetic-secret", ConfirmPassword = "synthetic-secret",
            FirstName = "  Анна ", LastName = " Иванова  ", TermsAccepted = true, MarketingConsent = marketing
        };
        RegistrationData? data = null;
        _email.Setup(s => s.AcceptInvitationAsync(Token, It.IsAny<RegistrationData>(), It.IsAny<CancellationToken>()))
            .Callback<string, RegistrationData, CancellationToken>((_, d, _) => data = d)
            .ReturnsAsync(new AccountEmailResult(true));
        var result = await _controller.AcceptInvitation(form);
        Assert.Equal("Completed", Assert.IsType<ViewResult>(result).ViewName);
        Assert.NotNull(data);
        Assert.True(data.TermsAccepted);
        Assert.Equal(marketing, data.MarketingConsent);
        Assert.Equal("Анна", data.FirstName);
        Assert.Equal("Иванова", data.LastName);
        Assert.Empty(data.Email);
    }

    [Fact]
    public async Task Confirmation_IsAnExplicitPostAction()
    {
        _email.Setup(s => s.ConfirmEmailAsync(Token, It.IsAny<CancellationToken>())).ReturnsAsync(new AccountEmailResult(true));
        var result = await _controller.ConfirmEmail(new AccountEmailTokenForm { Token = Token });
        Assert.Equal("Completed", Assert.IsType<ViewResult>(result).ViewName);
        _email.Verify(s => s.ConfirmEmailAsync(Token, It.IsAny<CancellationToken>()), Times.Once);
    }
}

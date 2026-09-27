using System.Security.Claims;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Algara.UnitTests.Identity;

public sealed class UserServiceSecurityStampTests : IDisposable
{
    private readonly IdentityDbContext _database = new(new DbContextOptions<IdentityDbContext>());
    private readonly Mock<IUserStore<ApplicationUser>> _users = new(MockBehavior.Strict);
    private readonly Mock<IAuthenticationService> _authentication = new(MockBehavior.Strict);
    private readonly DefaultHttpContext _http = new();
    private readonly ServiceProvider _services;
    private readonly UserService _service;

    public UserServiceSecurityStampTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_users.Object)
            .AddSingleton(_authentication.Object)
            .BuildServiceProvider();
        _http.RequestServices = _services;
        _authentication.Setup(a => a.SignOutAsync(_http, null, null)).Returns(Task.CompletedTask);
        _service = new UserService(_database, NullLogger<UserService>.Instance);
    }

    [Fact]
    public async Task ValidateSecurityStamp_NullPrincipal_SignsOutWithoutLookingUpUser()
    {
        var context = CreateContext();
        context.RejectPrincipal();

        await _service.ValidateSecurityStampAsync(context);

        AssertRejectedAndSignedOut(context);
        _users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("SecurityStamp")]
    [InlineData("SessionId")]
    public async Task ValidateSecurityStamp_MissingRequiredClaim_SignsOutWithoutLookingUpUser(string missingClaim)
    {
        var context = CreateContext(missingClaim);

        await _service.ValidateSecurityStampAsync(context);

        AssertRejectedAndSignedOut(context);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateSecurityStamp_UserNoLongerExists_RejectsCookieBeforeQueryingSessions()
    {
        var context = CreateContext();
        _users.Setup(s => s.FindByIdAsync("customer-id", CancellationToken.None))
            .ReturnsAsync((ApplicationUser?)null);

        // The EF context deliberately has no database provider. A session query here
        // would fail, so this also guards the order of the missing-user check.
        await _service.ValidateSecurityStampAsync(context);

        AssertRejectedAndSignedOut(context);
        _users.Verify(s => s.FindByIdAsync("customer-id", CancellationToken.None), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    private CookieValidatePrincipalContext CreateContext(string? missingClaim = null)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, "customer-id"),
            new("SecurityStamp", "security-stamp"),
            new("SessionId", "session-id")
        ];
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims.Where(c => c.Type != missingClaim), scheme.Name));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), scheme.Name);
        return new CookieValidatePrincipalContext(_http, scheme, new CookieAuthenticationOptions(), ticket);
    }

    private void AssertRejectedAndSignedOut(CookieValidatePrincipalContext context)
    {
        Assert.Null(context.Principal);
        _authentication.Verify(a => a.SignOutAsync(_http, null, null), Times.Once);
        _authentication.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        _database.Dispose();
        _services.Dispose();
    }
}

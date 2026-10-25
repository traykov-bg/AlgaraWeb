using System.Security.Claims;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Algara.UnitTests.Identity;

public sealed class UserServiceCredentialTransitionTests
{
    [Theory]
    [InlineData(false, false, false, "stamp")]
    [InlineData(true, true, false, "stamp")]
    [InlineData(true, false, false, null)]
    public async Task SignIn_IneligibleCredentialSnapshot_IsRejectedBeforeAnyDatabaseReloadOrCookie(
        bool active, bool requiresConfirmation, bool confirmed, string? stamp)
    {
        var user = User();
        user.IsActive = active;
        user.RequireEmailConfirmation = requiresConfirmation;
        user.EmailConfirmed = confirmed;
        user.SecurityStamp = stamp!;
        using var context = new IdentityServiceTestContext([user]);
        var service = new UserService(context, NullLogger<UserService>.Instance);

        // The context has no SQL connection or cookie authentication services; an unsafe path
        // beyond the eligibility gate would fail with a different exception or attempt I/O.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SignInAsync(new DefaultHttpContext(), user, false));

        Assert.Equal("The account is not eligible to sign in.", error.Message);
        Assert.Equal(0, context.SaveCount);
    }

    [Theory]
    [InlineData(false, false, false, "stamp")]
    [InlineData(true, true, false, "stamp")]
    [InlineData(true, false, false, null)]
    public async Task ChangePassword_IneligibleCredentialSnapshot_PreservesCredentialsAndDoesNotStartTransaction(
        bool active, bool requiresConfirmation, bool confirmed, string? stamp)
    {
        var user = User();
        user.IsActive = active;
        user.RequireEmailConfirmation = requiresConfirmation;
        user.EmailConfirmed = confirmed;
        user.SecurityStamp = stamp!;
        var originalHash = user.PasswordHash;
        using var context = new IdentityServiceTestContext([user]);

        var changed = await new UserService(context, NullLogger<UserService>.Instance)
            .ChangePasswordAsync(user.UserName, "new-password", new ClaimsPrincipal());

        Assert.False(changed);
        Assert.Equal(originalHash, user.PasswordHash);
        Assert.Equal(stamp, user.SecurityStamp);
        Assert.Equal(0, context.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public async Task ChangePassword_InvalidReplacement_CannotWrite(string? password)
    {
        using var context = new IdentityServiceTestContext([User()]);
        Assert.False(await new UserService(context, NullLogger<UserService>.Instance)
            .ChangePasswordAsync("customer@example.test", password!, new ClaimsPrincipal()));
        Assert.Equal(0, context.SaveCount);
    }

    [Fact]
    public async Task ChangePassword_ExcessiveReplacement_CannotWrite()
    {
        using var context = new IdentityServiceTestContext([User()]);
        Assert.False(await new UserService(context, NullLogger<UserService>.Instance)
            .ChangePasswordAsync("customer@example.test", new string('x', 1025), new ClaimsPrincipal()));
        Assert.Equal(0, context.SaveCount);
    }

    [Fact]
    public async Task ChangePassword_MissingUser_CannotWrite()
    {
        using var context = new IdentityServiceTestContext();
        Assert.False(await new UserService(context, NullLogger<UserService>.Instance)
            .ChangePasswordAsync("customer@example.test", "new-password", new ClaimsPrincipal()));
        Assert.Equal(0, context.SaveCount);
    }

    private static ApplicationUser User()
    {
        var credential = AccountPasswordCodec.Create("original-password");
        return new() { N = 7, UserName = "customer@example.test", Email = "customer@example.test", PasswordHash = credential.Hash,
            Salt = credential.Salt, SecurityStamp = "original-stamp", IsActive = true };
    }
}

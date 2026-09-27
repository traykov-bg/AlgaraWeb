using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Algara.UnitTests.Identity;

public sealed class UserServiceRoleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoleChange_SavesAssignmentAndNewSecurityStampTogether(bool add)
    {
        var user = User();
        var role = new ApplicationRole { N = 3, Name = "Admin" };
        using var context = new IdentityServiceTestContext([user], [role],
            add ? [] : [new UserRole { UserN = user.N, RoleN = role.N }]);
        context.OnSave = () =>
        {
            var assignment = Assert.Single(context.ChangeTracker.Entries<UserRole>());
            Assert.Equal(add ? EntityState.Added : EntityState.Deleted, assignment.State);
            Assert.Equal(user.N, assignment.Entity.UserN);
            Assert.Equal(role.N, assignment.Entity.RoleN);
            Assert.Equal(EntityState.Modified, context.Entry(user).State);
            Assert.True(context.Entry(user).Property(u => u.SecurityStamp).IsModified);
            Assert.NotEqual("old-stamp", user.SecurityStamp);
            Assert.True(Guid.TryParse(user.SecurityStamp, out _));
        };

        var changed = await ChangeRole(Service(context), add);

        Assert.True(changed);
        Assert.Equal(1, context.SaveCount);
        Assert.Equal("latest-session", user.LastLoginSessionId);
        Assert.Equal("customer-id", user.Id);
    }

    [Theory]
    [InlineData(true, "user")]
    [InlineData(false, "user")]
    [InlineData(true, "role")]
    [InlineData(false, "role")]
    [InlineData(true, "assignment")]
    [InlineData(false, "assignment")]
    public async Task RoleChange_NoChange_DoesNotSaveOrInvalidateSessions(bool add, string noChange)
    {
        var user = User();
        var role = new ApplicationRole { N = 3, Name = "Admin" };
        using var context = new IdentityServiceTestContext(
            noChange == "user" ? [] : [user],
            noChange == "role" ? [] : [role],
            noChange == "assignment" && add ? [new UserRole { UserN = user.N, RoleN = role.N }] : []);

        var changed = await ChangeRole(Service(context), add);

        Assert.False(changed);
        Assert.Equal(0, context.SaveCount);
        Assert.Equal("old-stamp", user.SecurityStamp);
        Assert.Equal("latest-session", user.LastLoginSessionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoleChange_FailedSave_PropagatesTheFailure(bool add)
    {
        var user = User();
        var role = new ApplicationRole { N = 3, Name = "Admin" };
        using var context = new IdentityServiceTestContext([user], [role],
            add ? [] : [new UserRole { UserN = user.N, RoleN = role.N }]);
        var failure = new DbUpdateException("Storage unavailable");
        context.OnSave = () => throw failure;

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => ChangeRole(Service(context), add));

        Assert.Same(failure, error);
        Assert.Equal(1, context.SaveCount);
    }

    private static ApplicationUser User() => new()
    {
        N = 7,
        Id = "customer-id",
        UserName = "customer@example.test",
        SecurityStamp = "old-stamp",
        LastLoginSessionId = "latest-session"
    };

    private static UserService Service(IdentityServiceTestContext context) => new(context, NullLogger<UserService>.Instance);

    private static Task<bool> ChangeRole(UserService service, bool add) => add
        ? service.AddUserToRoleAsync("customer@example.test", "Admin")
        : service.RemoveUserFromRoleAsync("customer@example.test", "Admin");
}

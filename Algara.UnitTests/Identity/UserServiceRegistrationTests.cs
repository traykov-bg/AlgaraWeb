using Algara.Identity.Models;
using Algara.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Algara.UnitTests.Identity;

public sealed class UserServiceRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registration_ExistingEmail_ReturnsExistingDuplicateOutcomeWithoutSaving(bool customer)
    {
        using var context = new IdentityServiceTestContext(
            [new ApplicationUser { N = 7, UserName = "another-name", Email = "customer@example.test" }]);
        var service = new UserService(context, NullLogger<UserService>.Instance);

        if (customer)
            Assert.Null(await service.RegisterUserAsync(Data()));
        else
            Assert.False(await service.RegisterUserAsync("new-name", "customer@example.test", "abcdef"));

        Assert.Equal(0, context.SaveCount);
        Assert.DoesNotContain(context.ChangeTracker.Entries(), entry => entry.State == EntityState.Added);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registration_UnrelatedSaveFailure_IsNotReportedAsDuplicateEmail(bool customer)
    {
        using var context = new IdentityServiceTestContext();
        var failure = new DbUpdateException("Unrelated persistence failure", new InvalidOperationException("Storage unavailable"));
        context.OnSave = () => throw failure;
        var service = new UserService(context, NullLogger<UserService>.Instance);

        var error = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            if (customer)
                await service.RegisterUserAsync(Data());
            else
                await service.RegisterUserAsync("new-name", "customer@example.test", "abcdef");
        });

        Assert.Same(failure, error);
        Assert.Equal(1, context.SaveCount);
        Assert.Equal(EntityState.Added, Assert.Single(context.ChangeTracker.Entries<ApplicationUser>()).State);
    }

    [Fact]
    public void IdentityModel_EnforcesBoundedUniqueEmail()
    {
        using var context = new IdentityServiceTestContext();
        var entity = context.Model.FindEntityType(typeof(ApplicationUser))!;
        var email = entity.FindProperty(nameof(ApplicationUser.Email))!;

        Assert.Equal(256, email.GetMaxLength());
        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.SequenceEqual([email]));
        Assert.True(index.IsUnique);
        Assert.Equal("IX_Users_Email", index.GetDatabaseName());
    }

    private static RegistrationData Data() => new()
    {
        Email = "customer@example.test",
        Password = "abcdef",
        FirstName = "Ива",
        LastName = "Иванова"
    };
}

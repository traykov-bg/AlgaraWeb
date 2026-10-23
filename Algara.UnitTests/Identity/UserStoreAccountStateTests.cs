using Algara.Identity.Models;
using Algara.Identity.Services;
using Moq;

namespace Algara.UnitTests.Identity;

public sealed class UserStoreAccountStateTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("number")]
    [InlineData("name")]
    [InlineData("role")]
    public async Task AccountLookup_SelectsPersistedEligibilityAndConfirmationFields(string lookup)
    {
        var database = new Mock<IDatabaseHelper>(MockBehavior.Strict);
        string? capturedSql = null;
        var expected = new ApplicationUser { IsActive = false, RequireEmailConfirmation = true, EmailConfirmed = false };
        if (lookup == "role")
        {
            database.Setup(db => db.QueryAsync<ApplicationUser>(It.IsAny<string>(), It.IsAny<object>()))
                .Callback<string, object?>((sql, _) => capturedSql = sql)
                .ReturnsAsync(new[] { expected });
        }
        else
        {
            database.Setup(db => db.QuerySingleAsync<ApplicationUser>(It.IsAny<string>(), It.IsAny<object>()))
                .Callback<string, object?>((sql, _) => capturedSql = sql)
                .ReturnsAsync(expected);
        }
        var store = new UserStore(database.Object);

        var actual = lookup switch
        {
            "id" => await store.FindByIdAsync("account-id", default),
            "number" => await store.FindByNAsync(17, default),
            "name" => await store.FindByNameAsync("customer@example.test", default),
            "role" => Assert.Single(await store.GetUsersInRoleAsync("User", default)),
            _ => throw new ArgumentOutOfRangeException(nameof(lookup))
        };

        Assert.Same(expected, actual);
        Assert.NotNull(capturedSql);
        // Dapper leaves omitted fields at their defaults. Selecting the persisted flags is
        // essential: cookie validation must not turn RequireEmailConfirmation=true into false.
        var from = capturedSql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
        Assert.True(from > 0);
        var columns = capturedSql[..from].Trim()["SELECT".Length..].Split(',')
            .Select(column => column.Trim().Split('.').Last())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var field in new[] { "IsActive", "SecurityStamp", "LastLoginSessionId",
            "RequireEmailConfirmation", "EmailConfirmed", "EmailConfirmedAtUtc" })
            Assert.Contains(field, columns);
    }
}

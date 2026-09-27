using Algara.Identity.Models;
using Algara.Identity.Services;
using Moq;

namespace Algara.UnitTests.Identity;

public sealed class UserStoreTests
{
    private readonly Mock<IDatabaseHelper> _database = new(MockBehavior.Strict);
    private readonly UserStore _store;

    public UserStoreTests() => _store = new UserStore(_database.Object);

    [Theory]
    [InlineData("id")]
    [InlineData("number")]
    [InlineData("name")]
    public async Task FindUser_NoMatchingRow_ReturnsNull(string lookup)
    {
        _database.Setup(db => db.QuerySingleAsync<ApplicationUser>(It.IsAny<string>(), It.IsAny<object>()))
            .ReturnsAsync((ApplicationUser?)null);

        var user = await FindUserAsync(lookup, CancellationToken.None);

        Assert.Null(user);
        _database.Verify(db => db.QuerySingleAsync<ApplicationUser>(It.IsAny<string>(), It.IsAny<object>()), Times.Once);
        _database.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("number")]
    [InlineData("name")]
    public async Task FindUser_Cancelled_DoesNotQueryDatabase(string lookup)
    {
        var cancellation = new CancellationToken(canceled: true);

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => FindUserAsync(lookup, cancellation));

        Assert.Equal(cancellation, error.CancellationToken);
        _database.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, "userName")]
    [InlineData(true, "normalizedName")]
    public async Task SetUserName_Null_DoesNotClearRequiredColumn(bool normalized, string parameter)
    {
        var user = new ApplicationUser { UserName = "customer@example.test" };

        var error = await Assert.ThrowsAsync<ArgumentNullException>(() => normalized
            ? _store.SetNormalizedUserNameAsync(user, null, CancellationToken.None)
            : _store.SetUserNameAsync(user, null, CancellationToken.None));

        Assert.Equal(parameter, error.ParamName);
        Assert.Equal("customer@example.test", user.UserName);
        _database.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, "CUSTOMER@EXAMPLE.TEST")]
    [InlineData(true, "customer@example.test")]
    public async Task SetUserName_ValidValue_PreservesExistingNormalizationBehavior(bool normalized, string expected)
    {
        var user = new ApplicationUser();

        if (normalized)
            await _store.SetNormalizedUserNameAsync(user, "CUSTOMER@EXAMPLE.TEST", CancellationToken.None);
        else
            await _store.SetUserNameAsync(user, "CUSTOMER@EXAMPLE.TEST", CancellationToken.None);

        Assert.Equal(expected, await _store.GetUserNameAsync(user, CancellationToken.None));
        _database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetPasswordHash_Null_ClearsPasswordWithoutChangingSaltOrWritingNull()
    {
        var user = new ApplicationUser { PasswordHash = "existing-hash", Salt = "existing-salt" };

        await _store.SetPasswordHashAsync(user, null, CancellationToken.None);

        Assert.Equal(string.Empty, await _store.GetPasswordHashAsync(user, CancellationToken.None));
        Assert.False(await _store.HasPasswordAsync(user, CancellationToken.None));
        Assert.Equal("existing-salt", user.Salt);
        _database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetPasswordHash_Value_PreservesHashAndSaltWithoutRehashing()
    {
        var user = new ApplicationUser { Salt = "existing-salt" };

        await _store.SetPasswordHashAsync(user, "stored-hash", CancellationToken.None);

        Assert.Equal("stored-hash", await _store.GetPasswordHashAsync(user, CancellationToken.None));
        Assert.True(await _store.HasPasswordAsync(user, CancellationToken.None));
        Assert.Equal("existing-salt", user.Salt);
        _database.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("normalizedName")]
    [InlineData("passwordHash")]
    public async Task Setter_Cancelled_DoesNotMutateUser(string setter)
    {
        var cancellation = new CancellationToken(canceled: true);
        var user = new ApplicationUser
        {
            UserName = "customer@example.test",
            PasswordHash = "existing-hash",
            Salt = "existing-salt"
        };

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => setter switch
        {
            "name" => _store.SetUserNameAsync(user, "changed@example.test", cancellation),
            "normalizedName" => _store.SetNormalizedUserNameAsync(user, "changed@example.test", cancellation),
            "passwordHash" => _store.SetPasswordHashAsync(user, null, cancellation),
            _ => throw new ArgumentOutOfRangeException(nameof(setter))
        });

        Assert.Equal(cancellation, error.CancellationToken);
        Assert.Equal("customer@example.test", user.UserName);
        Assert.Equal("existing-hash", user.PasswordHash);
        Assert.Equal("existing-salt", user.Salt);
        _database.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoleChange_Cancelled_DoesNotQueryOrMutateUser(bool add)
    {
        var user = RoleUser();
        var cancellation = new CancellationToken(canceled: true);

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => ChangeRoleAsync(user, add, cancellation));

        Assert.Equal(cancellation, error.CancellationToken);
        Assert.Equal("old-stamp", user.SecurityStamp);
        Assert.Equal("existing-session", user.LastLoginSessionId);
        _database.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoleChange_FailedDatabaseCommand_DoesNotChangeInMemoryStamp(bool add)
    {
        var user = RoleUser();
        var failure = new InvalidOperationException("Storage unavailable");
        _database.Setup(db => db.QuerySingleAsync<string>(It.IsAny<string>(), It.IsAny<object>()))
            .ThrowsAsync(failure);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ChangeRoleAsync(user, add, CancellationToken.None));

        Assert.Same(failure, error);
        Assert.Equal("old-stamp", user.SecurityStamp);
        Assert.Equal("existing-session", user.LastLoginSessionId);
        _database.Verify(db => db.QuerySingleAsync<string>(It.IsAny<string>(), It.IsAny<object>()), Times.Once);
        _database.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoleChange_NoChangedDatabaseRow_PreservesInMemoryStamp(bool add)
    {
        var user = RoleUser();
        _database.Setup(db => db.QuerySingleAsync<string>(It.IsAny<string>(), It.IsAny<object>()))
            .ReturnsAsync((string?)null);

        await ChangeRoleAsync(user, add, CancellationToken.None);

        Assert.Equal("old-stamp", user.SecurityStamp);
        Assert.Equal("existing-session", user.LastLoginSessionId);
        _database.Verify(db => db.QuerySingleAsync<string>(It.IsAny<string>(), It.IsAny<object>()), Times.Once);
        _database.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoleChange_ConfirmedDatabaseChange_UsesPersistedStampForSameUser(bool add)
    {
        var user = RoleUser();
        object? commandParameters = null;
        _database.Setup(db => db.QuerySingleAsync<string>(It.IsAny<string>(), It.IsAny<object>()))
            .Callback((string _, object? parameters) => commandParameters = parameters)
            .ReturnsAsync("confirmed-stamp");

        await ChangeRoleAsync(user, add, CancellationToken.None);

        Assert.NotNull(commandParameters);
        Assert.Equal(user.N, Parameter<int>(commandParameters, "UserN"));
        Assert.Equal("Admin", Parameter<string>(commandParameters, "RoleName"));
        var proposedStamp = Parameter<string>(commandParameters, "SecurityStamp");
        Assert.True(Guid.TryParse(proposedStamp, out _));
        Assert.NotEqual("old-stamp", proposedStamp);
        Assert.Equal("confirmed-stamp", user.SecurityStamp);
        Assert.Equal("existing-session", user.LastLoginSessionId);
        _database.Verify(db => db.QuerySingleAsync<string>(It.IsAny<string>(), It.IsAny<object>()), Times.Once);
        _database.VerifyNoOtherCalls();
    }

    private Task ChangeRoleAsync(ApplicationUser user, bool add, CancellationToken cancellation) => add
        ? _store.AddToRoleAsync(user, "Admin", cancellation)
        : _store.RemoveFromRoleAsync(user, "Admin", cancellation);

    private static ApplicationUser RoleUser() => new()
    {
        N = 7,
        SecurityStamp = "old-stamp",
        LastLoginSessionId = "existing-session"
    };

    private static T Parameter<T>(object parameters, string name) =>
        (T)parameters.GetType().GetProperty(name)!.GetValue(parameters)!;

    private Task<ApplicationUser?> FindUserAsync(string lookup, CancellationToken cancellation) => lookup switch
    {
        "id" => _store.FindByIdAsync("customer-id", cancellation),
        "number" => _store.FindByNAsync(7, cancellation),
        "name" => _store.FindByNameAsync("CUSTOMER@EXAMPLE.TEST", cancellation),
        _ => throw new ArgumentOutOfRangeException(nameof(lookup))
    };
}

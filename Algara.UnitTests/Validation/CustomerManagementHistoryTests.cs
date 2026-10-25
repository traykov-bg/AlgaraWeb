using Algara.Web.ViewModels.Admin;

namespace Algara.UnitTests.Validation;

public sealed class CustomerManagementHistoryTests
{
    [Fact]
    public void Profile_OnlyShowsChangedAllowedContactFields()
    {
        var result = CustomerManagementHistory.Format("ProfileUpdated",
            """{"FirstName":"Old","LastName":"Same","PhoneNumber":null,"SecurityStamp":"secret-old","PasswordHash":"secret"}""",
            """{"FirstName":"New","LastName":"Same","PhoneNumber":"12345","SecurityStamp":"secret-new","PasswordHash":"changed"}""");
        Assert.Collection(result,
            change => { Assert.Equal("Име", change.Label); Assert.Equal("Old", change.Before); Assert.Equal("New", change.After); },
            change => { Assert.Equal("Телефон", change.Label); Assert.Equal("—", change.Before); Assert.Equal("12345", change.After); });
    }

    [Fact]
    public void Profile_RecordsDerivedNameRepairsEvenWhenContactFieldsAreUnchanged()
    {
        var result = CustomerManagementHistory.Format("ProfileUpdated",
            """{"FirstName":"Iva","LastName":"Ivanova","FullName":"Old name","DisplayName":"Old display"}""",
            """{"FirstName":"Iva","LastName":"Ivanova","FullName":"Iva Ivanova","DisplayName":"Iva Ivanova"}""");
        Assert.Collection(result,
            change => { Assert.Equal("Пълно име", change.Label); Assert.Equal("Old name", change.Before); Assert.Equal("Iva Ivanova", change.After); },
            change => { Assert.Equal("Показвано име", change.Label); Assert.Equal("Old display", change.Before); Assert.Equal("Iva Ivanova", change.After); });
    }

    [Fact]
    public void AddressBook_MatchesByNumericIdAndReportsDefaultChangesWithoutRawSnapshots()
    {
        var result = CustomerManagementHistory.Format("DefaultAddressChanged",
            """[{"N":8,"City":"Sofia","IsDefault":true,"SessionId":"secret"},{"N":9,"City":"Varna","IsDefault":false}]""",
            """[{"N":9,"City":"Varna","IsDefault":true},{"N":8,"City":"Sofia","IsDefault":false,"SessionId":"changed"}]""");
        Assert.Collection(result,
            change => { Assert.Equal("Адрес #8 · Основен адрес", change.Label); Assert.Equal("Да", change.Before); Assert.Equal("Не", change.After); },
            change => { Assert.Equal("Адрес #9 · Основен адрес", change.Label); Assert.Equal("Не", change.Before); Assert.Equal("Да", change.After); });
    }

    [Theory]
    [InlineData("AddressCreated", null, "[{\"N\":7,\"City\":\"Sofia\"}]", "—", "Sofia")]
    [InlineData("AddressDeleted", "[{\"N\":7,\"City\":\"Sofia\"}]", null, "Sofia", "—")]
    public void AddressCreateAndDelete_KeepSafeBeforeAfter(string action, string? before, string? after, string expectedBefore, string expectedAfter)
    {
        var change = Assert.Single(CustomerManagementHistory.Format(action, before, after));
        Assert.Equal("Адрес #7 · Град", change.Label);
        Assert.Equal(expectedBefore, change.Before); Assert.Equal(expectedAfter, change.After);
    }

    [Fact]
    public void SessionHistory_OnlyShowsNumericIdentifierAndRevocationState()
    {
        var result = CustomerManagementHistory.Format("SessionRevoked",
            """{"SessionN":12,"IsActive":true,"SessionId":"secret","DeviceInfo":"private"}""",
            """{"SessionN":12,"IsActive":false,"SessionId":"secret-new","DeviceInfo":"changed"}""");
        var change = Assert.Single(result);
        Assert.Equal("Непрекратена", change.Label); Assert.Equal("Да", change.Before); Assert.Equal("Не", change.After);
    }

    [Fact]
    public void AllSessions_UsesSafeCountOnly()
    {
        var change = Assert.Single(CustomerManagementHistory.Format("AllSessionsRevoked", "{\"ActiveSessionCount\":5}", "{\"ActiveSessionCount\":0}"));
        Assert.Equal("Непрекратени сесии", change.Label); Assert.Equal("5", change.Before); Assert.Equal("0", change.After);
    }

    [Fact]
    public void AccessHistory_FormatsUtcTimestampAndIgnoresCredentials()
    {
        var result = CustomerManagementHistory.Format("AccessSuspended",
            """{"IsActive":true,"AccessSuspendedAtUtc":null,"SecurityStamp":"old"}""",
            """{"IsActive":false,"AccessSuspendedAtUtc":"2026-10-23T09:15:00Z","SecurityStamp":"new"}""");
        Assert.Equal(2, result.Count);
        Assert.Equal("23.10.2026 09:15 UTC", result[1].After);
    }

    [Theory]
    [InlineData("ProfileUpdated", "not-json", "{}")]
    [InlineData("ProfileUpdated", "{}", "not-json")]
    [InlineData("UnknownAction", "{\"PasswordHash\":\"old\"}", "{\"PasswordHash\":\"new\"}")]
    [InlineData("AddressUpdated", "[]", "[{\"N\":\"bad\",\"City\":\"hidden\"}]")]
    [InlineData("AddressUpdated", "[]", "[{\"N\":999999999999999,\"City\":\"hidden\"}]")]
    [InlineData("ProfileUpdated", "{}", "{\"FirstName\":{\"PasswordHash\":\"secret\"}}")]
    public void MalformedOrUnknownPayloads_DoNotLeakRawJsonOrThrow(string action, string before, string after)
        => Assert.Empty(CustomerManagementHistory.Format(action, before, after));
}

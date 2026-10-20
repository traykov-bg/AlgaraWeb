using Algara.Data.Models;
using Algara.Identity.Models;
using Algara.Web.Services;

namespace Algara.UnitTests.Services;

public sealed class CustomerDossierVersionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("AAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAB=")]
    [InlineData("AAAAAAAAAAA=\n")]
    [InlineData(" AAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAA")]
    [InlineData("AAAAAAAAAA!=")]
    public void RowVersion_RejectsMissingWrongLengthMalformedAndNoncanonicalValues(string? value)
    {
        Assert.False(CustomerDossierVersions.IsRowVersion(value));
    }

    [Fact]
    public void RowVersion_AcceptsExactlyEightBytesInCanonicalBase64()
    {
        var version = Convert.ToBase64String([0, 1, 2, 3, 254, 255, 128, 127]);
        Assert.True(CustomerDossierVersions.IsRowVersion(version));
        Assert.True(CustomerDossierVersions.IsLinkVersion(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("NONE")]
    [InlineData(" none")]
    [InlineData("none ")]
    [InlineData("AAAAAAAAAAB=")]
    public void LinkVersion_DoesNotInterpretMissingOrMalformedInputAsUnlinked(string? value)
    {
        Assert.False(CustomerDossierVersions.IsLinkVersion(value));
    }

    [Fact]
    public void LinkVersion_DistinguishesNeverLinkedFromExistingUnlinkedRow()
    {
        var previousLink = new PartnerAccountLink { UserN = 5, PartnerN = null, RowVersion = [0, 0, 0, 0, 0, 0, 0, 7] };
        Assert.Equal("none", CustomerDossierVersions.Link(null));
        Assert.True(CustomerDossierVersions.IsLinkVersion("none"));
        Assert.Equal("AAAAAAAAAAc=", CustomerDossierVersions.Link(previousLink));
        Assert.NotEqual(CustomerDossierVersions.Link(null), CustomerDossierVersions.Link(previousLink));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("AAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\n")]
    public void AccountVersion_RejectsMissingWrongLengthAndNoncanonicalValues(string? value)
    {
        Assert.False(CustomerDossierVersions.IsAccountVersion(value));
    }

    [Fact]
    public void AccountVersion_IsStableCanonicalAndOpaqueForTheSameAccount()
    {
        var user = Account();
        var version = CustomerDossierVersions.Account(user);
        Assert.True(CustomerDossierVersions.IsAccountVersion(version));
        Assert.Equal(version, CustomerDossierVersions.Account(Account()));
        Assert.Equal(32, Convert.FromBase64String(version).Length);
        Assert.DoesNotContain(user.Email, version);
    }

    [Theory]
    [InlineData(nameof(ApplicationUser.Id))]
    [InlineData(nameof(ApplicationUser.UserName))]
    [InlineData(nameof(ApplicationUser.Email))]
    [InlineData(nameof(ApplicationUser.DisplayName))]
    [InlineData(nameof(ApplicationUser.FirstName))]
    [InlineData(nameof(ApplicationUser.LastName))]
    [InlineData(nameof(ApplicationUser.PhoneNumber))]
    [InlineData(nameof(ApplicationUser.SecurityStamp))]
    public void AccountVersion_ChangesWhenIdentityOrContactFieldChanges(string propertyName)
    {
        var user = Account();
        var version = CustomerDossierVersions.Account(user);
        typeof(ApplicationUser).GetProperty(propertyName)!.SetValue(user, "changed-value");
        Assert.NotEqual(version, CustomerDossierVersions.Account(user));
    }

    [Fact]
    public void AccountVersion_ChangesWhenAccountIdOrActiveStateChanges()
    {
        var user = Account();
        var version = CustomerDossierVersions.Account(user);
        user.N++;
        Assert.NotEqual(version, CustomerDossierVersions.Account(user));
        user = Account();
        user.IsActive = false;
        Assert.NotEqual(version, CustomerDossierVersions.Account(user));
    }

    [Fact]
    public void AccountVersion_PreservesLegacyEmptyStampAndMissingContactDistinctions()
    {
        var user = Account();
        user.SecurityStamp = string.Empty;
        var legacy = CustomerDossierVersions.Account(user);
        Assert.True(CustomerDossierVersions.IsAccountVersion(legacy));
        user.SecurityStamp = null!;
        Assert.NotEqual(legacy, CustomerDossierVersions.Account(user));
        user = Account();
        user.PhoneNumber = null;
        var missing = CustomerDossierVersions.Account(user);
        user.PhoneNumber = string.Empty;
        Assert.NotEqual(missing, CustomerDossierVersions.Account(user));
    }

    private static ApplicationUser Account() => new()
    {
        N = 42, Id = "account-id", UserName = "customer@example.test", Email = "customer@example.test",
        DisplayName = "Customer", FirstName = "First", LastName = "Last", PhoneNumber = "+359888123456",
        IsActive = true, SecurityStamp = "account-stamp"
    };
}

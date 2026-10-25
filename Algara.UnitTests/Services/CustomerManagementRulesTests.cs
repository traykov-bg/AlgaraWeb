using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Web.Services;

namespace Algara.UnitTests.Services;

public sealed class CustomerManagementRulesTests
{
    [Fact]
    public void Profile_TrimsNamesAndNormalizesEmptyPhoneWithoutChangingIdentityFields()
    {
        var request = new CustomerProfileUpdate { FirstName = "  Ива  ", LastName = "  Иванова  ", PhoneNumber = "  " };
        Assert.Null(CustomerManagementRules.Validate(request));
        Assert.Equal("Ива", request.FirstName);
        Assert.Equal("Иванова", request.LastName);
        Assert.Null(request.PhoneNumber);
    }

    [Theory]
    [InlineData("", "Family", null)]
    [InlineData("First", " ", null)]
    [InlineData("First\nInjected", "Family", null)]
    [InlineData("First", "Family", "invalid-phone")]
    [InlineData("First", "Family", "123\r456")]
    public void Profile_RejectsInvalidNamesOrPhone(string firstName, string lastName, string? phone)
    {
        Assert.NotNull(CustomerManagementRules.Validate(new CustomerProfileUpdate
        {
            FirstName = firstName, LastName = lastName, PhoneNumber = phone
        }));
    }

    [Fact]
    public void Profile_EnforcesNameAndPhoneBounds()
    {
        var request = new CustomerProfileUpdate { FirstName = new string('a', 100), LastName = new string('b', 100), PhoneNumber = new string('1', 40) };
        Assert.Null(CustomerManagementRules.Validate(request));
        request.FirstName += "a";
        Assert.NotNull(CustomerManagementRules.Validate(request));
        request.FirstName = "First";
        request.PhoneNumber += "1";
        Assert.NotNull(CustomerManagementRules.Validate(request));
    }

    [Fact]
    public void Address_NormalizesOptionalFieldsAndAcceptsSeparateRecipientEmail()
    {
        var request = Address();
        request.PhoneNumber = "  "; request.AddressLine2 = " "; request.PostalCode = " ";
        request.Email = "  recipient@example.test  ";
        Assert.Null(CustomerManagementRules.Validate(request));
        Assert.Equal("recipient@example.test", request.Email);
        Assert.Null(request.PhoneNumber); Assert.Null(request.AddressLine2); Assert.Null(request.PostalCode);
    }

    [Theory]
    [InlineData("field", "")]
    [InlineData("email", "bad-address")]
    [InlineData("email", "Display Name <recipient@example.test>")]
    [InlineData("email", "recipient@example.test\r\nBcc:another@example.test")]
    [InlineData("city", "Sofia\nInjected")]
    [InlineData("country", "")]
    public void Address_RejectsInvalidRequiredFields(string field, string value)
    {
        var request = Address();
        switch (field)
        {
            case "field": request.AddressLine1 = value; break;
            case "email": request.Email = value; break;
            case "city": request.City = value; break;
            case "country": request.Country = value; break;
        }
        Assert.NotNull(CustomerManagementRules.Validate(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Address_RejectsPostedNonpositiveExistingIdentifier(int id)
    {
        var request = Address(); request.N = id;
        Assert.NotNull(CustomerManagementRules.Validate(request));
    }

    [Fact]
    public void Address_EnforcesContactAndAddressLimits()
    {
        var request = Address(); request.AddressLine1 = new string('a', 250); request.AddressLine2 = new string('b', 250);
        request.City = new string('c', 100); request.Country = new string('d', 100); request.PostalCode = new string('1', 20);
        Assert.Null(CustomerManagementRules.Validate(request));
        request.AddressLine1 += "a"; Assert.NotNull(CustomerManagementRules.Validate(request));
        request.AddressLine1 = "Street"; request.PostalCode += "1"; Assert.NotNull(CustomerManagementRules.Validate(request));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" Проверено искане ", true)]
    public void StaffReason_IsMandatory(string? reason, bool valid) => Assert.Equal(valid, CustomerManagementRules.ValidReason(reason));

    [Fact]
    public void StaffReason_AllowsExactlyTwoThousandCharacters()
    {
        Assert.True(CustomerManagementRules.ValidReason(new string('a', 2000)));
        Assert.False(CustomerManagementRules.ValidReason(new string('a', 2001)));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Eligibility_DistinguishesReversibleSuspensionFromLegacyInactive(bool active, bool marker, bool allowed)
    {
        var user = new ApplicationUser { IsActive = active, AccessSuspendedAtUtc = marker ? DateTime.UnixEpoch : null };
        Assert.Equal(allowed, CustomerManagementRules.CanManage(user));
    }

    [Theory]
    [InlineData(PermissionCatalog.Users.EditProfile)]
    [InlineData(PermissionCatalog.Users.ManageAddresses)]
    [InlineData(PermissionCatalog.Users.Unlock)]
    [InlineData(PermissionCatalog.Users.SuspendAccess)]
    [InlineData(PermissionCatalog.Users.ViewSessions)]
    [InlineData(PermissionCatalog.Users.RevokeSessions)]
    public void NewPermissions_RequireDossierAndReceiveNoNonAdminDefaultGrant(string permission)
    {
        Assert.NotNull(PermissionCatalog.Find(permission));
        Assert.True(PermissionCatalog.RequiresCustomerDossier(permission));
        Assert.Contains(permission, PermissionCatalog.DefaultGrants(StaffRoles.Admin));
        foreach (var role in StaffRoles.All.Where(r => r != StaffRoles.Admin)) Assert.DoesNotContain(permission, PermissionCatalog.DefaultGrants(role));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("stamp")]
    [InlineData("activity")]
    [InlineData("suspension")]
    [InlineData("attempts")]
    [InlineData("lockout")]
    [InlineData("confirmation")]
    public void AccountVersion_RejectsRelevantConcurrentStateChanges(string field)
    {
        var user = new ApplicationUser { N = 5, SecurityStamp = string.Empty };
        var version = CustomerManagementVersions.Account(user);
        Assert.True(CustomerManagementVersions.IsVersion(version));
        switch (field)
        {
            case "profile": user.PhoneNumber = "12345"; break;
            case "stamp": user.SecurityStamp = "new-version"; break;
            case "activity": user.IsActive = false; break;
            case "suspension": user.AccessSuspendedAtUtc = DateTime.UnixEpoch; break;
            case "attempts": user.FailedLoginAttempts = 2; break;
            case "lockout": user.LockoutUntil = DateTime.UnixEpoch; break;
            case "confirmation": user.EmailConfirmed = true; break;
        }
        Assert.NotEqual(version, CustomerManagementVersions.Account(user));
    }

    [Fact]
    public void AccountVersion_DoesNotEmbedCredentialsOrCoupleUnrelatedConsentChanges()
    {
        var user = new ApplicationUser { N = 5, SecurityStamp = string.Empty };
        var version = CustomerManagementVersions.Account(user);
        user.PasswordHash = "synthetic-credential"; user.Salt = "synthetic-salt"; user.MarketingConsent = true;
        Assert.Equal(version, CustomerManagementVersions.Account(user));
        Assert.DoesNotContain("synthetic", version);
    }

    [Fact]
    public void AddressBookVersion_IsOrderIndependentButDetectsOwnershipDefaultAndContentChanges()
    {
        var first = new UserAddress { N = 1, UserN = 5, AddressLine1 = "First", IsDefault = true, CreatedAt = DateTime.UnixEpoch };
        var second = new UserAddress { N = 2, UserN = 5, AddressLine1 = "Second", CreatedAt = DateTime.UnixEpoch };
        var version = CustomerManagementVersions.AddressBook([first, second]);
        Assert.Equal(version, CustomerManagementVersions.AddressBook([second, first]));
        second.AddressLine1 = "Changed"; Assert.NotEqual(version, CustomerManagementVersions.AddressBook([first, second]));
        second.AddressLine1 = "Second"; second.IsDefault = true; Assert.NotEqual(version, CustomerManagementVersions.AddressBook([first, second]));
        second.IsDefault = false; second.UserN = 6; Assert.NotEqual(version, CustomerManagementVersions.AddressBook([first, second]));
        Assert.NotEqual(version, CustomerManagementVersions.AddressBook([first]));
    }

    [Fact]
    public void EmptyAddressBook_HasCanonicalVersionAndChangesWhenAddressAppears()
    {
        var empty = CustomerManagementVersions.AddressBook([]);
        Assert.True(CustomerManagementVersions.IsVersion(empty));
        Assert.NotEqual(empty, CustomerManagementVersions.AddressBook([new UserAddress { N = 1, UserN = 5 }]));
    }

    [Fact]
    public void SessionVersion_TracksRowOwnershipAndRevocationWithoutExposingSessionKey()
    {
        var session = new UserSession { Id = 17, UserN = 5, SessionId = "synthetic-secret-key", IsActive = true, CreatedAt = DateTime.UnixEpoch };
        var version = CustomerManagementVersions.Session(session);
        Assert.True(CustomerManagementVersions.IsVersion(version));
        Assert.DoesNotContain("synthetic", version);
        session.IsActive = false; Assert.NotEqual(version, CustomerManagementVersions.Session(session));
        session.IsActive = true; session.UserN = 6; Assert.NotEqual(version, CustomerManagementVersions.Session(session));
        session.UserN = 5; session.Id = 18; Assert.NotEqual(version, CustomerManagementVersions.Session(session));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("AAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA= ")]
    public void Versions_RejectMissingMalformedOrNoncanonicalInput(string? value) => Assert.False(CustomerManagementVersions.IsVersion(value));

    private static CustomerAddressUpdate Address() => new()
    {
        FirstName = "Ива", LastName = "Иванова", Email = "recipient@example.test",
        AddressLine1 = "Примерна улица 1", City = "София", Country = "България"
    };
}

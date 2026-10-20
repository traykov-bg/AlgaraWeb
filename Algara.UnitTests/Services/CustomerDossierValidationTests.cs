using System.Security.Claims;
using Algara.Data.Data;
using Algara.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Algara.UnitTests.Services;

public sealed class CustomerDossierValidationTests
{
    private static readonly string AccountVersion = Convert.ToBase64String(new byte[32]);
    private static readonly string RowVersion = Convert.ToBase64String(new byte[8]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public async Task EmptyNote_IsRejectedBeforeDatabaseAccess(string? note)
    {
        using var db = DatabaseWithoutProvider();
        AssertRejected(await new CustomerDossierService(db).AddNoteAsync(new ClaimsPrincipal(), 1, AccountVersion, note));
    }

    [Fact]
    public async Task OversizedNote_IsRejectedBeforeDatabaseAccess()
    {
        using var db = DatabaseWithoutProvider();
        AssertRejected(await new CustomerDossierService(db).AddNoteAsync(new ClaimsPrincipal(), 1, AccountVersion, new string('n', 4001)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("AAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB=")]
    public async Task AllMutations_RejectInvalidAccountVersionBeforeDatabaseAccess(string? version)
    {
        using var db = DatabaseWithoutProvider();
        var service = new CustomerDossierService(db);
        AssertRejected(await service.AddNoteAsync(new ClaimsPrincipal(), 1, version, "Staff note"));
        AssertRejected(await service.LinkAsync(new ClaimsPrincipal(), 1, 2, version, "none", RowVersion, true, true, "in_person", "Verified request"));
        AssertRejected(await service.UnlinkAsync(new ClaimsPrincipal(), 1, version, RowVersion, "Verified request"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AllMutations_RejectInvalidAccountIdBeforeDatabaseAccess(int userN)
    {
        using var db = DatabaseWithoutProvider();
        var service = new CustomerDossierService(db);
        AssertRejected(await service.AddNoteAsync(new ClaimsPrincipal(), userN, AccountVersion, "Staff note"));
        AssertRejected(await service.LinkAsync(new ClaimsPrincipal(), userN, 2, AccountVersion, "none", RowVersion, true, true, "in_person", "Verified request"));
        AssertRejected(await service.UnlinkAsync(new ClaimsPrincipal(), userN, AccountVersion, RowVersion, "Verified request"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Linking_RequiresBothCustomerRequestAndSpecificPartnerConfirmation(bool specificPartner, bool customerRequest)
    {
        using var db = DatabaseWithoutProvider();
        AssertRejected(await new CustomerDossierService(db).LinkAsync(new ClaimsPrincipal(), 1, 2,
            AccountVersion, "none", RowVersion, specificPartner, customerRequest, "in_person", "Verified request"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("email_match")]
    [InlineData("phone_match")]
    [InlineData("IN_PERSON")]
    [InlineData("in_person ")]
    public async Task Linking_RejectsUnsupportedVerificationIncludingAutomaticContactMatches(string? method)
    {
        using var db = DatabaseWithoutProvider();
        AssertRejected(await new CustomerDossierService(db).LinkAsync(new ClaimsPrincipal(), 1, 2,
            AccountVersion, "none", RowVersion, true, true, method, "Verified request"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public async Task LinkingAndUnlinking_RequireRecordedReason(string? reason)
    {
        using var db = DatabaseWithoutProvider();
        var service = new CustomerDossierService(db);
        AssertRejected(await service.LinkAsync(new ClaimsPrincipal(), 1, 2,
            AccountVersion, "none", RowVersion, true, true, "in_person", reason));
        AssertRejected(await service.UnlinkAsync(new ClaimsPrincipal(), 1, AccountVersion, RowVersion, reason));
    }

    [Fact]
    public async Task LinkingAndUnlinking_RejectOversizedReasonBeforeDatabaseAccess()
    {
        using var db = DatabaseWithoutProvider();
        var service = new CustomerDossierService(db);
        var reason = new string('r', 2001);
        AssertRejected(await service.LinkAsync(new ClaimsPrincipal(), 1, 2,
            AccountVersion, "none", RowVersion, true, true, "in_person", reason));
        AssertRejected(await service.UnlinkAsync(new ClaimsPrincipal(), 1, AccountVersion, RowVersion, reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("AAAAAAAAAAB=")]
    public async Task LinkingAndUnlinking_RejectMissingOrMalformedCurrentLinkVersion(string? version)
    {
        using var db = DatabaseWithoutProvider();
        var service = new CustomerDossierService(db);
        AssertRejected(await service.LinkAsync(new ClaimsPrincipal(), 1, 2,
            AccountVersion, version, RowVersion, true, true, "in_person", "Verified request"));
        AssertRejected(await service.UnlinkAsync(new ClaimsPrincipal(), 1, AccountVersion, version, "Verified request"));
    }

    [Fact]
    public async Task Unlinking_RequiresAnExistingLinkVersion()
    {
        using var db = DatabaseWithoutProvider();
        AssertRejected(await new CustomerDossierService(db).UnlinkAsync(new ClaimsPrincipal(), 1, AccountVersion, "none", "Verified request"));
    }

    [Theory]
    [InlineData(0, "AAAAAAAAAAA=")]
    [InlineData(-1, "AAAAAAAAAAA=")]
    [InlineData(2, null)]
    [InlineData(2, "")]
    [InlineData(2, "none")]
    [InlineData(2, "AAAAAAAAAAB=")]
    public async Task Linking_RequiresAnExplicitPartnerWithCurrentCanonicalVersion(int partnerN, string? version)
    {
        using var db = DatabaseWithoutProvider();
        AssertRejected(await new CustomerDossierService(db).LinkAsync(new ClaimsPrincipal(), 1, partnerN,
            AccountVersion, "none", version, true, true, "in_person", "Verified request"));
    }

    private static ShopDbContext DatabaseWithoutProvider() => new(new DbContextOptions<ShopDbContext>());

    private static void AssertRejected(CustomerDossierResult result)
    {
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
    }
}

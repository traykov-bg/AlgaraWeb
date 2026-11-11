using System.Security.Claims;
using System.Text;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Web.Services.Inquiries;
using Algara.Web.Services.Email;
using Algara.Web.Services.Checkout;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.UnitTests.Services;
public sealed class InquiryTests
{
    private static InquiryForm Form() => new() { ContactName = "Test Customer", Phone = "+359888123456", Description = "Custom table", Consent = true };
    [Fact] public void GuestWithoutEmailCanSubmit()
    {
        var form = Form(); Assert.True(InquiryService.Valid(form));
        form.Email = " "; Assert.True(InquiryService.Valid(form));
        form.Email = "bad-email"; Assert.False(InquiryService.Valid(form));
    }
    [Fact] public void StaffNeedsReasonAndRealChannelInsteadOfCustomerConsent()
    {
        var form = Form(); form.Staff = true; form.Consent = false;
        Assert.False(InquiryService.Valid(form)); form.Reason = "Called the shop";
        Assert.True(InquiryService.Valid(form)); form.Channel = "Web"; Assert.False(InquiryService.Valid(form));
    }
    [Fact] public void CustomerMustAcknowledgePrivacy()
    {
        var form = Form(); form.Consent = false; Assert.False(InquiryService.Valid(form));
    }
    [Theory]
    [InlineData("", "description")]
    [InlineData("test\r\nname", "description")]
    [InlineData("test", "  ")]
    public void InvalidContactsAndBlankDescriptionRejected(string name, string description)
    {
        var form = Form(); form.ContactName = name; form.Description = description; Assert.False(InquiryService.Valid(form));
    }
    [Theory]
    [InlineData(InquiryStatus.New, "Offer", true, InquiryStatus.Offered)]
    [InlineData(InquiryStatus.Offered, "Offer", true, InquiryStatus.Offered)]
    [InlineData(InquiryStatus.Offered, "Accept", false, InquiryStatus.Accepted)]
    [InlineData(InquiryStatus.Accepted, "Offer", true, InquiryStatus.Offered)]
    [InlineData(InquiryStatus.New, "Complete", true, InquiryStatus.Accepted)]
    public void AllowedTransitions(InquiryStatus from, string command, bool staff, InquiryStatus expected) => Assert.Equal(expected, InquiryService.Next(from, command, staff));
    [Theory]
    [InlineData(InquiryStatus.New, "Accept", false)]
    [InlineData(InquiryStatus.New, "Offer", false)]
    [InlineData(InquiryStatus.Accepted, "Close", true)]
    [InlineData(InquiryStatus.Closed, "Start", true)]
    [InlineData(InquiryStatus.Offered, "Assign", false)]
    [InlineData(InquiryStatus.Offered, "Close", false)]
    public void ForbiddenTransitions(InquiryStatus from, string command, bool staff) => Assert.Null(InquiryService.Next(from, command, staff));
    [Fact] public void GuestSecretIsScopedToOneInquiry()
    {
        var token = AccountEmailRules.Issue(); var other = AccountEmailRules.Issue();
        var inquiry = new FurnitureInquiry { SubmissionId = token.Id, GuestTokenHash = token.Hash };
        Assert.True(InquiryService.HasGuestAccess(inquiry, token.LinkValue));
        Assert.False(InquiryService.HasGuestAccess(inquiry, other.LinkValue));
        Assert.False(InquiryService.HasGuestAccess(inquiry, token.LinkValue[..^1]));
        inquiry.SubmissionId = other.Id; Assert.False(InquiryService.HasGuestAccess(inquiry, token.LinkValue));
    }
    [Fact] public void SubmissionCannotCrossStaffCustomerOrCheckoutModes()
    {
        var actor = new ClaimsPrincipal(new ClaimsIdentity());
        var tokens = new CheckoutTokens(new EphemeralDataProtectionProvider(), TimeProvider.System);
        var token = tokens.Issue(InquiryService.Owner(actor, false));
        Assert.NotNull(tokens.Read(token, InquiryService.Owner(actor, false)));
        Assert.Null(tokens.Read(token, InquiryService.Owner(actor, true)));
        Assert.Null(tokens.Read(token, CheckoutService.Owner(actor)));
    }
    [Theory]
    [InlineData("<html>not pdf</html>")]
    [InlineData("%PDF-1.7 truncated content")]
    [InlineData("%PDF-1.7\nstartxref\n0\n%%EOF<script>alert(1)</script>")]
    public void PdfEnvelopeRejectsNonPdfAndTruncation(string input) => Assert.False(InquiryFiles.LooksLikePdf(Encoding.ASCII.GetBytes(input)));
    [Theory]
    [InlineData(StaffRoles.Moderator)]
    [InlineData(StaffRoles.CatalogEditor)]
    [InlineData(StaffRoles.OrderManager)]
    public void InquiryRightsRequireExplicitGrants(string role) => Assert.DoesNotContain(PermissionCatalog.DefaultGrants(role), x => x.StartsWith("Inquiries."));
}

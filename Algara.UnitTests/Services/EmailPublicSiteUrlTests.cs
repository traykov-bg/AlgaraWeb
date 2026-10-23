using Algara.Identity.Models;
using Algara.Web.Services.Email;
using Algara.UnitTests.Identity;
using Microsoft.EntityFrameworkCore;

namespace Algara.UnitTests.Services;

public sealed class EmailPublicSiteUrlTests
{
    [Theory]
    [InlineData("https://example.test")]
    [InlineData("https://example.test/")]
    [InlineData("https://example.test:8443")]
    [InlineData("http://localhost:5205")]
    [InlineData("http://127.0.0.1:5205")]
    [InlineData("http://[::1]:5205")]
    public void SiteUrl_AcceptsHttpsOriginsAndExplicitLoopbackHttp(string value) =>
        Assert.True(SmtpSettingsRules.TryPublicSiteUrl(value, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//example.test")]
    [InlineData("http://example.test")]
    [InlineData("http://localhost.example.test")]
    [InlineData("http://127.1")]
    [InlineData("http://2130706433")]
    [InlineData("https://example.test/path")]
    [InlineData("https://example.test?next=host")]
    [InlineData("https://example.test#fragment")]
    [InlineData("https://user:secret@example.test")]
    [InlineData("https://example.test\r\n")]
    [InlineData("https://example.test\\path")]
    [InlineData("javascript:alert(1)")]
    public void SiteUrl_RejectsAmbiguousOrUnsafeOrigins(string? value) =>
        Assert.False(SmtpSettingsRules.TryPublicSiteUrl(value, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t\r\n ")]
    public void SavingSmtpSettingsWithoutSiteUrl_NormalizesToExistingNonNullStorage(string? value)
    {
        var update = Valid();
        update.PublicSiteUrl = value;

        Assert.Null(SmtpSettingsRules.Validate(update));
        Assert.Equal(string.Empty, update.PublicSiteUrl);
        Assert.False(SmtpSettingsRules.TryPublicSiteUrl(update.PublicSiteUrl, out _));
    }

    [Fact]
    public void UnconfiguredSiteUrl_DefaultMatchesRequiredDatabaseColumn()
    {
        using var context = new IdentityServiceTestContext();
        var property = context.Model.FindEntityType(typeof(SmtpConfiguration))!
            .FindProperty(nameof(SmtpConfiguration.PublicSiteUrl))!;

        Assert.False(property.IsNullable);
        Assert.Equal(2048, property.GetMaxLength());
        Assert.Equal(string.Empty, new SmtpConfiguration().PublicSiteUrl);
    }

    [Fact]
    public void SavingSmtpSettingsWithoutSiteUrl_RemainsAllowedButReadinessRequiresEnabledSecret()
    {
        var update = Valid();
        Assert.Null(SmtpSettingsRules.Validate(update));
        update.PublicSiteUrl = "http://remote.example.test";
        Assert.NotNull(SmtpSettingsRules.Validate(update));
        update.PublicSiteUrl = "  https://example.test/  ";
        Assert.Null(SmtpSettingsRules.Validate(update));
        Assert.Equal("https://example.test/", update.PublicSiteUrl);
        var settings = new SmtpConfiguration { Host = "smtp.example.test", Port = 587, Username = "sender@example.test", FromEmail = "sender@example.test", ProtectedPassword = "protected", Enabled = false };
        Assert.False(EmailDeliveryReadiness.TryGetPassword(settings, new FakeSecrets(), out _));
        settings.Enabled = true;
        Assert.True(EmailDeliveryReadiness.TryGetPassword(settings, new FakeSecrets(), out _));
        settings.SecurityMode = SmtpSecurityMode.ImplicitTls;
        Assert.False(EmailDeliveryReadiness.TryGetPassword(settings, new FakeSecrets(), out _));
    }

    private static SmtpSettingsUpdate Valid() => new() { Host = "smtp.example.test", Port = 587, Username = "sender@example.test", FromEmail = "sender@example.test", ExpectedVersion = "none" };
    private sealed class FakeSecrets : ISmtpSecretProtector
    {
        public string Protect(string password) => "protected";
        public bool TryUnprotect(string protectedPassword, out string password) { password = "synthetic-only"; return true; }
    }
}

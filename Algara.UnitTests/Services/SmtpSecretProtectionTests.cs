using Algara.Identity.Models;
using Algara.Web.Services.Email;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.UnitTests.Services;

public sealed class SmtpSecretProtectionTests
{
    [Fact]
    public void ProtectedCredential_RoundTripsWithoutPersistingPlaintext()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = new SmtpSecretProtector(provider);
        const string password = "synthetic-credential-only";
        var encrypted = protector.Protect(password);
        Assert.NotEqual(password, encrypted);
        Assert.DoesNotContain(password, encrypted);
        Assert.True(protector.TryUnprotect(encrypted, out var decrypted));
        Assert.Equal(password, decrypted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-protected-data")]
    [InlineData("synthetic-plaintext-password")]
    public void InvalidStoredCiphertext_IsRejectedWithoutReturningSecret(string storedValue)
    {
        var protector = new SmtpSecretProtector(new EphemeralDataProtectionProvider());
        Assert.False(protector.TryUnprotect(storedValue, out var password));
        Assert.Empty(password);
    }

    [Fact]
    public void ChangedKeyRingCannotReadStoredCredential()
    {
        var oldProtector = new SmtpSecretProtector(new EphemeralDataProtectionProvider());
        var currentProtector = new SmtpSecretProtector(new EphemeralDataProtectionProvider());
        var encrypted = oldProtector.Protect("synthetic-credential-only");
        Assert.False(currentProtector.TryUnprotect(encrypted, out var password));
        Assert.Empty(password);
    }

    [Fact]
    public void OtherProtectionPurposeCannotBecomeAnSmtpCredential()
    {
        var provider = new EphemeralDataProtectionProvider();
        var otherPurpose = provider.CreateProtector("Algara.UnrelatedPurpose");
        var encrypted = otherPurpose.Protect("synthetic-credential-only");
        Assert.False(new SmtpSecretProtector(provider).TryUnprotect(encrypted, out var password));
        Assert.Empty(password);
    }

    [Theory]
    [InlineData(SmtpSecurityMode.StartTls, SecureSocketOptions.StartTls)]
    [InlineData(SmtpSecurityMode.ImplicitTls, SecureSocketOptions.SslOnConnect)]
    public void ConnectionProbe_RequiresStrictTlsWithoutOptionalUpgrade(SmtpSecurityMode mode, SecureSocketOptions expected)
    {
        var socketOptions = SmtpConnectionProbe.SocketOptions(mode);
        Assert.Equal(expected, socketOptions);
        Assert.NotEqual(SecureSocketOptions.Auto, socketOptions);
        Assert.NotEqual(SecureSocketOptions.None, socketOptions);
        Assert.NotEqual(SecureSocketOptions.StartTlsWhenAvailable, socketOptions);
    }

    [Theory]
    [InlineData((SmtpSecurityMode)0)]
    [InlineData((SmtpSecurityMode)3)]
    [InlineData((SmtpSecurityMode)(-1))]
    public void ConnectionProbe_RejectsUnsupportedSecurityModes(SmtpSecurityMode mode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SmtpConnectionProbe.SocketOptions(mode));
    }
}

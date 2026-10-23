using Algara.Identity.Models;
using Algara.Web.Services.Email;

namespace Algara.UnitTests.Services;

public sealed class SmtpSettingsRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("none ")]
    [InlineData("AAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAB=")]
    [InlineData("AAAAAAAAAAA=\n")]
    public void Version_RejectsMissingMalformedAndNoncanonicalValues(string? version)
    {
        Assert.False(SmtpSettingsRules.IsVersion(version));
    }

    [Fact]
    public void Version_DistinguishesNoConfigurationFromAnExistingVersionedRow()
    {
        Assert.True(SmtpSettingsRules.IsVersion("none"));
        Assert.Equal("none", SmtpSettingsRules.Version(null));
        var current = new SmtpConfiguration { RowVersion = [0, 1, 2, 3, 4, 5, 6, 7] };
        var version = SmtpSettingsRules.Version(current);
        Assert.NotEqual("none", version);
        Assert.True(SmtpSettingsRules.IsVersion(version));
        Assert.Equal(current.RowVersion, Convert.FromBase64String(version));
    }

    [Fact]
    public void GmailStartTls_NormalizesDisplayFieldsAndGroupedSyntheticAppPassword()
    {
        var update = ValidUpdate();
        update.Host = " SMTP.GMAIL.COM ";
        update.Username = " sender@example.test ";
        update.FromEmail = " sender@example.test ";
        update.FromName = " Test sender ";
        update.Password = "abcd efgh ijkl mnop";
        Assert.Null(SmtpSettingsRules.Validate(update));
        Assert.Equal("smtp.gmail.com", update.Host);
        Assert.Equal("sender@example.test", update.Username);
        Assert.Equal("sender@example.test", update.FromEmail);
        Assert.Equal("Test sender", update.FromName);
        Assert.Equal("abcdefghijklmnop", update.Password);
    }

    [Theory]
    [InlineData("smtp.gmail.com", 587, SmtpSecurityMode.StartTls)]
    [InlineData("smtp.gmail.com", 465, SmtpSecurityMode.ImplicitTls)]
    [InlineData("smtp.example.test", 2525, SmtpSecurityMode.StartTls)]
    [InlineData("smtp.example.test", 2465, SmtpSecurityMode.ImplicitTls)]
    public void ValidSecureEndpoints_AreAccepted(string host, int port, SmtpSecurityMode mode)
    {
        var update = ValidUpdate();
        update.Host = host;
        update.Port = port;
        update.SecurityMode = mode;
        Assert.Null(SmtpSettingsRules.Validate(update));
    }

    [Theory]
    [InlineData(587, SmtpSecurityMode.ImplicitTls)]
    [InlineData(465, SmtpSecurityMode.StartTls)]
    [InlineData(25, (SmtpSecurityMode)0)]
    [InlineData(25, (SmtpSecurityMode)3)]
    [InlineData(0, SmtpSecurityMode.StartTls)]
    [InlineData(65536, SmtpSecurityMode.StartTls)]
    public void InvalidPortOrUnsafeSecurityCombination_IsRejected(int port, SmtpSecurityMode mode)
    {
        var update = ValidUpdate();
        update.Port = port;
        update.SecurityMode = mode;
        Assert.NotNull(SmtpSettingsRules.Validate(update));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://smtp.example.test")]
    [InlineData("smtp.example.test:587")]
    [InlineData("smtp.example.test/path")]
    [InlineData("smtp.example.test.")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("smtp.\r\nexample.test")]
    public void Host_RejectsUrlsIpAddressesAndControlCharacters(string host)
    {
        var update = ValidUpdate();
        update.Host = host;
        Assert.NotNull(SmtpSettingsRules.Validate(update));
    }

    [Theory]
    [InlineData("Name <sender@example.test>")]
    [InlineData("sender@example.test,other@example.test")]
    [InlineData("sender@example.test\r\nBcc: other@example.test")]
    [InlineData("invalid-email")]
    public void Sender_RequiresOnePlainMailboxWithoutHeaderInjection(string fromEmail)
    {
        var update = ValidUpdate();
        update.FromEmail = fromEmail;
        Assert.NotNull(SmtpSettingsRules.Validate(update));
    }

    [Theory]
    [InlineData(nameof(SmtpSettingsUpdate.Username), "sender\r\nAUTH LOGIN")]
    [InlineData(nameof(SmtpSettingsUpdate.FromName), "Sender\r\nBcc: other@example.test")]
    [InlineData(nameof(SmtpSettingsUpdate.Password), "synthetic\npassword")]
    public void CredentialAndHeaderFields_RejectEmbeddedControls(string propertyName, string value)
    {
        var update = ValidUpdate();
        typeof(SmtpSettingsUpdate).GetProperty(propertyName)!.SetValue(update, value);
        Assert.NotNull(SmtpSettingsRules.Validate(update));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void BlankPassword_StaysAnExplicitPreserveInstruction(string? password)
    {
        var update = ValidUpdate();
        update.Password = password;
        Assert.Null(SmtpSettingsRules.Validate(update));
        Assert.Null(update.Password);
        Assert.False(update.RemovePassword);
    }

    [Fact]
    public void NonGmailPassword_PreservesMeaningfulSpaces()
    {
        var update = ValidUpdate();
        update.Host = "smtp.example.test";
        update.Password = " synthetic password ";
        Assert.Null(SmtpSettingsRules.Validate(update));
        Assert.Equal(" synthetic password ", update.Password);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("abcdefghijklmn!p")]
    [InlineData("abcdefghijklmn\u00e9p")]
    public void Gmail_RejectsValuesThatAreNotAppPasswordShape(string password)
    {
        var update = ValidUpdate();
        update.Password = password;
        Assert.NotNull(SmtpSettingsRules.Validate(update));
    }

    [Fact]
    public void PasswordRemoval_RequiresDisabledSendingAndCannotAlsoSupplyReplacement()
    {
        var update = ValidUpdate();
        update.RemovePassword = true;
        update.Enabled = true;
        Assert.NotNull(SmtpSettingsRules.Validate(update));
        update.Enabled = false;
        update.Password = "abcdefghijklmnop";
        Assert.NotNull(SmtpSettingsRules.Validate(update));
        update.Password = null;
        Assert.Null(SmtpSettingsRules.Validate(update));
    }

    [Theory]
    [InlineData(nameof(SmtpSettingsUpdate.Host), 254)]
    [InlineData(nameof(SmtpSettingsUpdate.Username), 257)]
    [InlineData(nameof(SmtpSettingsUpdate.FromEmail), 257)]
    [InlineData(nameof(SmtpSettingsUpdate.FromName), 201)]
    [InlineData(nameof(SmtpSettingsUpdate.Password), 1025)]
    public void OversizedSettings_AreRejectedBeforePersistence(string propertyName, int length)
    {
        var update = ValidUpdate();
        typeof(SmtpSettingsUpdate).GetProperty(propertyName)!.SetValue(update, new string('a', length));
        Assert.NotNull(SmtpSettingsRules.Validate(update));
    }

    [Fact]
    public void ChangedCredentialDestination_RequiresSeparateSecretReuseDecision()
    {
        var current = Current();
        var update = ValidUpdate();
        Assert.False(SmtpSettingsRules.EndpointChanged(current, update));
        update.Host = "SMTP.GMAIL.COM";
        update.FromEmail = "different@example.test";
        update.FromName = "Different sender";
        update.Enabled = true;
        Assert.False(SmtpSettingsRules.EndpointChanged(current, update));

        update = ValidUpdate();
        update.Host = "different.example.test";
        Assert.True(SmtpSettingsRules.EndpointChanged(current, update));
        update = ValidUpdate();
        update.Port = 2525;
        Assert.True(SmtpSettingsRules.EndpointChanged(current, update));
        update = ValidUpdate();
        update.SecurityMode = SmtpSecurityMode.ImplicitTls;
        Assert.True(SmtpSettingsRules.EndpointChanged(current, update));
        update = ValidUpdate();
        update.Username = "different@example.test";
        Assert.True(SmtpSettingsRules.EndpointChanged(current, update));
    }

    [Fact]
    public void UpdateToString_DoesNotIncludeWriteOnlyPassword()
    {
        var update = ValidUpdate();
        update.Password = "synthetic-secret-for-test";
        Assert.DoesNotContain(update.Password, update.ToString()!);
    }

    private static SmtpSettingsUpdate ValidUpdate() => new()
    {
        Host = "smtp.gmail.com", Port = 587, SecurityMode = SmtpSecurityMode.StartTls,
        Username = "sender@example.test", FromEmail = "sender@example.test", FromName = "Test sender",
        ExpectedVersion = "none"
    };

    private static SmtpConfiguration Current() => new()
    {
        Host = "smtp.gmail.com", Port = 587, SecurityMode = SmtpSecurityMode.StartTls,
        Username = "sender@example.test", FromEmail = "sender@example.test", FromName = "Test sender"
    };
}

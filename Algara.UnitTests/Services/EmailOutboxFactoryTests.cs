using Algara.Identity.Models;
using Algara.Web.Services.Email;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.UnitTests.Services;

public sealed class EmailOutboxFactoryTests
{
    private static EmailOutboxFactory Factory() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Payload_IsProtectedAndBoundToTheMessageRecipientSubjectAndToken()
    {
        var factory = Factory();
        var message = factory.Create("recipient@example.test", "Subject", "Private body", "https://example.test/Account/ResetPassword#test-token", DateTime.UtcNow.AddHours(1), Guid.NewGuid());
        Assert.DoesNotContain("test-token", message.ProtectedPayload!);
        Assert.DoesNotContain("Private body", message.ProtectedPayload!);
        Assert.True(factory.TryReadPayload(message, out var read));
        Assert.Equal("Private body", read.Text);
        Assert.EndsWith("#test-token", read.ActionUrl);
        message.Recipient = "someoneelse@example.test";
        Assert.False(factory.TryReadPayload(message, out _));
        message.Recipient = "recipient@example.test";
        message.Subject = "Another subject";
        Assert.False(factory.TryReadPayload(message, out _));
        message.Subject = "Subject";
        message.TokenId = Guid.NewGuid();
        Assert.False(factory.TryReadPayload(message, out _));
    }

    [Fact]
    public void Payload_RejectsCorruptionAndUnrelatedKeyRings()
    {
        var factory = Factory();
        var message = factory.Create("recipient@example.test", "Subject", "Body", null, null, null);
        Assert.False(Factory().TryReadPayload(message, out _));
        message.ProtectedPayload = "corrupted";
        Assert.False(factory.TryReadPayload(message, out _));
        message.ProtectedPayload = null;
        Assert.False(factory.TryReadPayload(message, out _));
    }

    [Theory]
    [InlineData("Name <recipient@example.test>")]
    [InlineData("recipient@example.test,other@example.test")]
    [InlineData("recipient@example.test\r\nBcc: other@example.test")]
    [InlineData("invalid")]
    public void Create_RejectsAmbiguousOrInjectedRecipients(string recipient) =>
        Assert.Throws<ArgumentException>(() => Factory().Create(recipient, "Subject", "Body", null, null, null));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.test/reset#token")]
    [InlineData("https://user:password@example.test/reset#token")]
    [InlineData("/relative/path")]
    public void Create_RejectsUnsafeActionUrls(string url) =>
        Assert.Throws<ArgumentException>(() => Factory().Create("recipient@example.test", "Subject", "Body", url, null, null));

    [Fact]
    public void Create_RejectsExpiredMessagesAndHeaderInjection()
    {
        var factory = Factory();
        Assert.Throws<ArgumentException>(() => factory.Create("recipient@example.test", "Subject", "Body", null, DateTime.UtcNow.AddSeconds(-1), null));
        Assert.Throws<ArgumentException>(() => factory.Create("recipient@example.test", "Subject\r\nBcc: other@example.test", "Body", null, null, null));
    }

    [Fact]
    public void MimeMessage_EncodesHtmlAndHasOnlyTheConfiguredRecipientWithAStableMessageId()
    {
        var message = new EmailOutboxMessage { Recipient = "recipient@example.test", Subject = "Subject", CreatedAtUtc = DateTime.UtcNow };
        var payload = new EmailOutboxPayload { Text = "<script>alert('x')</script>\nSecond line", ActionUrl = "https://example.test/Account/Reset#token\"&value" };
        var settings = new SmtpConfiguration { FromEmail = "sender@example.test", FromName = "Алгара" };
        using var first = AccountSmtpEmailSender.BuildMessage(settings, message, payload);
        using var retry = AccountSmtpEmailSender.BuildMessage(settings, message, payload);
        Assert.Equal(first.MessageId, retry.MessageId);
        Assert.Equal("recipient@example.test", Assert.Single(first.To.Mailboxes).Address);
        Assert.Empty(first.Cc);
        Assert.Empty(first.Bcc);
        Assert.DoesNotContain("<script>", first.HtmlBody);
        Assert.Contains("&lt;script&gt;", first.HtmlBody);
        Assert.Contains("&quot;&amp;value", first.HtmlBody);
        Assert.Contains(payload.Text, first.TextBody!.Replace("\r\n", "\n"));
        Assert.Contains(payload.ActionUrl, first.TextBody);
    }

    [Fact]
    public void MimeMessage_RejectsAnUnsafeHrefEvenWhenCalledWithoutTheFactory()
    {
        Assert.Throws<ArgumentException>(() => AccountSmtpEmailSender.BuildMessage(
            new SmtpConfiguration { FromEmail = "sender@example.test" },
            new EmailOutboxMessage { Recipient = "recipient@example.test", Subject = "Subject" },
            new EmailOutboxPayload { Text = "Body", ActionUrl = "javascript:alert(1)" }));
    }
}

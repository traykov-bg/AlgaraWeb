using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using Algara.Identity.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.Web.Services.Email;

// Deliberately not a record: generated ToString must not include a token-bearing URL.
public sealed class EmailOutboxPayload
{
    public string Text { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
}

public sealed class EmailOutboxFactory(IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("Algara.EmailOutbox.Payload.v1");

    public EmailOutboxMessage Create(string recipient, string subject, string text, string? actionUrl,
        DateTime? expiresAtUtc, Guid? tokenId)
    {
        if (!IsMailbox(recipient)) throw new ArgumentException("Invalid recipient.", nameof(recipient));
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 200 || subject.Any(char.IsControl))
            throw new ArgumentException("Invalid subject.", nameof(subject));
        if (string.IsNullOrWhiteSpace(text) || text.Length > 8000)
            throw new ArgumentException("Invalid message body.", nameof(text));
        if (actionUrl != null && !IsActionUrl(actionUrl))
            throw new ArgumentException("Invalid action URL.", nameof(actionUrl));
        var now = DateTime.UtcNow;
        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= now)
            throw new ArgumentException("Message already expired.", nameof(expiresAtUtc));
        var message = new EmailOutboxMessage
        {
            Recipient = recipient, Subject = subject, TokenId = tokenId,
            CreatedAtUtc = now, NextAttemptAtUtc = now, ExpiresAtUtc = expiresAtUtc
        };
        // Bind the protected body to the immutable recipient, subject and row ID as well as its purpose.
        message.ProtectedPayload = ForMessage(message).Protect(JsonSerializer.Serialize(new EmailOutboxPayload { Text = text, ActionUrl = actionUrl }));
        if (message.ProtectedPayload.Length > 32768)
            throw new ArgumentException("Message body is too large.", nameof(text));
        return message;
    }

    public bool TryReadPayload(EmailOutboxMessage message, out EmailOutboxPayload payload)
    {
        payload = new();
        if (message.ProtectedPayload == null) return false;
        try
        {
            var decoded = JsonSerializer.Deserialize<EmailOutboxPayload>(ForMessage(message).Unprotect(message.ProtectedPayload));
            if (decoded == null || string.IsNullOrWhiteSpace(decoded.Text) || decoded.Text.Length > 8000 ||
                (decoded.ActionUrl != null && !IsActionUrl(decoded.ActionUrl))) return false;
            payload = decoded;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return false; }
    }

    private IDataProtector ForMessage(EmailOutboxMessage message) => protector.CreateProtector(
        message.Id.ToString("N"), message.Recipient, message.Subject, message.TokenId?.ToString("N") ?? "none");

    public static bool IsMailbox(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        !value.Any(char.IsControl) && MailAddress.TryCreate(value, out var address) && address.Address == value;

    public static bool IsActionUrl(string value)
    {
        if (value.Length > 4096 || value.Any(char.IsControl) || value.Contains('\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0) return false;
        return SmtpSettingsRules.TryPublicSiteUrl(uri.GetLeftPart(UriPartial.Authority), out _);
    }
}

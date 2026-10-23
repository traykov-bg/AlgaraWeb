using System.Net;
using System.Security.Authentication;
using Algara.Identity.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Algara.Web.Services.Email;

public sealed record EmailSendResult(bool Accepted, bool Retryable = false, string? ErrorCode = null);

public interface IAccountEmailSender
{
    Task<EmailSendResult> SendAsync(SmtpConfiguration settings, string password, EmailOutboxMessage message,
        EmailOutboxPayload payload, CancellationToken cancellationToken);
}

public sealed class AccountSmtpEmailSender : IAccountEmailSender
{
    public static MimeMessage BuildMessage(SmtpConfiguration settings, EmailOutboxMessage message, EmailOutboxPayload payload)
    {
        if (!EmailOutboxFactory.IsMailbox(settings.FromEmail) || !EmailOutboxFactory.IsMailbox(message.Recipient) ||
            settings.FromName.Any(char.IsControl) || message.Subject.Any(char.IsControl) ||
            (payload.ActionUrl != null && !EmailOutboxFactory.IsActionUrl(payload.ActionUrl)))
            throw new ArgumentException("Invalid mail headers.");
        var mail = new MimeMessage
        {
            MessageId = $"outbox-{message.Id:N}@algara.invalid",
            Date = new DateTimeOffset(DateTime.SpecifyKind(message.CreatedAtUtc, DateTimeKind.Utc)),
            Subject = message.Subject
        };
        mail.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        mail.To.Add(MailboxAddress.Parse(message.Recipient));
        var plainText = payload.Text;
        var html = "<!doctype html><html lang=\"bg\"><body><p>" +
            WebUtility.HtmlEncode(payload.Text).Replace("\r\n", "\n").Replace("\n", "<br>") + "</p>";
        if (payload.ActionUrl != null)
        {
            plainText += "\n\n" + payload.ActionUrl;
            html += "<p><a href=\"" + WebUtility.HtmlEncode(payload.ActionUrl) + "\">Продължи в Алгара</a></p>";
        }
        html += "</body></html>";
        mail.Body = new BodyBuilder { TextBody = plainText, HtmlBody = html }.ToMessageBody();
        return mail;
    }

    public async Task<EmailSendResult> SendAsync(SmtpConfiguration settings, string password, EmailOutboxMessage message,
        EmailOutboxPayload payload, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var client = new SmtpClient
        {
            Timeout = 15000,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CheckCertificateRevocation = true
        };
        try
        {
            using var mail = BuildMessage(settings, message, payload);
            await client.ConnectAsync(settings.Host, settings.Port, SmtpConnectionProbe.SocketOptions(settings.SecurityMode), deadline.Token);
            if (!client.IsSecure) return new(false, ErrorCode: "smtp_tls_required");
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            await client.AuthenticateAsync(settings.Username, password, deadline.Token);
            await client.SendAsync(mail, deadline.Token);
            // A positive DATA reply means accepted by SMTP, not delivered to a mailbox.
            return new(true);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (MailKit.Security.AuthenticationException) { return new(false, ErrorCode: "smtp_authentication_failed"); }
        catch (Exception ex) when (ex is SslHandshakeException or System.Security.Authentication.AuthenticationException or NotSupportedException)
        {
            return new(false, ErrorCode: "smtp_tls_failed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, true, "smtp_timeout");
        }
        catch (SmtpCommandException ex)
        {
            return new(false, (int)ex.StatusCode is >= 400 and < 500, "smtp_rejected");
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or SmtpProtocolException)
        {
            return new(false, true, "smtp_connection_failed");
        }
        catch (ArgumentException) { return new(false, ErrorCode: "invalid_message"); }
    }
}

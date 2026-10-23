using System.Security.Authentication;
using Algara.Identity.Models;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Algara.Web.Services.Email;

public sealed class SmtpConnectionProbe : ISmtpConnectionProbe
{
    public static SecureSocketOptions SocketOptions(SmtpSecurityMode mode) => mode switch
    {
        SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurityMode.ImplicitTls => SecureSocketOptions.SslOnConnect,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public async Task<SmtpSettingsResult> CheckAsync(string host, int port, SmtpSecurityMode securityMode,
        string username, string password, CancellationToken cancellationToken)
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
            // Never use Auto/StartTlsWhenAvailable and never bypass certificate validation.
            await client.ConnectAsync(host, port, SocketOptions(securityMode), deadline.Token);
            if (!client.IsSecure) return new(false, "SMTP сървърът не установи защитена TLS връзка.");
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            await client.AuthenticateAsync(username, password, deadline.Token);
            // This checks authentication only: no MAIL FROM, RCPT TO or DATA is sent.
            return new(true);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (MailKit.Security.AuthenticationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(false, "SMTP удостоверяването е отказано. Проверете потребителското име и паролата за приложение.");
        }
        catch (Exception ex) when (ex is SslHandshakeException or System.Security.Authentication.AuthenticationException or NotSupportedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(false, "Не може да се установи валидна защитена TLS връзка. Проверете сървъра, порта и режима.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "SMTP проверката надвиши 15 секунди. Проверете настройките и свързаността.");
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or SmtpCommandException or SmtpProtocolException)
        {
            // SMTP reply/exception text can contain credentials or untrusted server content.
            cancellationToken.ThrowIfCancellationRequested();
            return new(false, "Връзката със SMTP сървъра е неуспешна. Проверете настройките и свързаността.");
        }
    }
}

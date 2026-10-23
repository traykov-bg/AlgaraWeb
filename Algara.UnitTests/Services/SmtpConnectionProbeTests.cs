using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Algara.Identity.Models;
using Algara.Web.Services.Email;

namespace Algara.UnitTests.Services;

public sealed class SmtpConnectionProbeTests
{
    private const string Username = "loopback-only@example.test";
    private const string Password = "synthetic-loopback-secret";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartTls_ServerAdvertisingAuthWithoutStartTls_IsRejectedBeforeCredentialsAreSent(bool delivery)
    {
        var commands = new List<string>();
        await using var server = new LoopbackServer(async (stream, token) =>
        {
            using var reader = CreateReader(stream);
            await ReplyAsync(stream, "220 localhost SMTP fixture\r\n", token);
            var ehlo = await ReadCommandAsync(reader, commands, token);
            Assert.StartsWith("EHLO ", ehlo, StringComparison.OrdinalIgnoreCase);
            await ReplyAsync(stream, "250-localhost\r\n250 AUTH PLAIN LOGIN\r\n", token);
            await DrainCommandsAsync(reader, stream, commands, token);
        });

        var result = await InvokeAsync(delivery, server.Port,
            SmtpSecurityMode.StartTls, CancellationToken.None).WaitAsync(TestTimeout);
        await server.Completion.WaitAsync(TestTimeout);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
        Assert.Single(commands);
        AssertNoCredentials(commands, result);
    }

    [Theory]
    [InlineData(SmtpSecurityMode.StartTls, false)]
    [InlineData(SmtpSecurityMode.ImplicitTls, false)]
    [InlineData(SmtpSecurityMode.StartTls, true)]
    [InlineData(SmtpSecurityMode.ImplicitTls, true)]
    public async Task UntrustedSelfSignedCertificate_IsRejectedBeforeAuthentication(SmtpSecurityMode mode, bool delivery)
    {
        using var certificate = CreateUntrustedCertificate();
        var commands = new List<string>();
        var attemptedHandshake = false;
        await using var server = new LoopbackServer(async (stream, token) =>
        {
            if (mode == SmtpSecurityMode.StartTls)
            {
                using var plaintextReader = CreateReader(stream);
                await ReplyAsync(stream, "220 localhost SMTP fixture\r\n", token);
                var ehlo = await ReadCommandAsync(plaintextReader, commands, token);
                Assert.StartsWith("EHLO ", ehlo, StringComparison.OrdinalIgnoreCase);
                await ReplyAsync(stream, "250-localhost\r\n250-STARTTLS\r\n250 AUTH PLAIN LOGIN\r\n", token);
                Assert.Equal("STARTTLS", await ReadCommandAsync(plaintextReader, commands, token));
                await ReplyAsync(stream, "220 Ready to start TLS\r\n", token);
            }

            using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
            attemptedHandshake = true;
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, token);
                // Some TLS implementations finish the server handshake before receiving the
                // client's rejection alert. If validation regresses, capture any SMTP AUTH.
                if (mode == SmtpSecurityMode.ImplicitTls)
                    await ReplyAsync(tls, "220 localhost SMTP fixture\r\n", token);
                using var encryptedReader = CreateReader(tls);
                await DrainCommandsAsync(encryptedReader, tls, commands, token);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException)
            {
                // A peer rejecting this deliberately untrusted certificate may send an alert
                // or close the socket during either the handshake or the next stream operation.
            }
        });

        var result = await InvokeAsync(delivery, server.Port,
            mode, CancellationToken.None).WaitAsync(TestTimeout);
        await server.Completion.WaitAsync(TestTimeout);

        Assert.True(attemptedHandshake);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
        AssertNoCredentials(commands, result);
        if (mode == SmtpSecurityMode.StartTls)
            Assert.Equal(2, commands.Count);
        else
            Assert.Empty(commands);
    }

    [Theory]
    [InlineData(SmtpSecurityMode.StartTls, false)]
    [InlineData(SmtpSecurityMode.ImplicitTls, false)]
    [InlineData(SmtpSecurityMode.StartTls, true)]
    [InlineData(SmtpSecurityMode.ImplicitTls, true)]
    public async Task CallerCancellation_InterruptsAConnectedButUnresponsiveServer(SmtpSecurityMode mode, bool delivery)
    {
        var protocolStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LoopbackServer(async (stream, token) =>
        {
            if (mode == SmtpSecurityMode.ImplicitTls)
            {
                var firstByte = new byte[1];
                Assert.Equal(1, await stream.ReadAsync(firstByte, token));
                Assert.Equal(0x16, firstByte[0]); // TLS ClientHello has actually started.
            }
            protocolStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        using var cancellation = new CancellationTokenSource();
        var pending = InvokeAsync(delivery, server.Port, mode, cancellation.Token);
        await server.ConnectionAccepted.WaitAsync(TestTimeout);
        await protocolStarted.Task.WaitAsync(TestTimeout);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pending.WaitAsync(TestTimeout));
    }

    private static async Task<SmtpSettingsResult> InvokeAsync(bool delivery, int port, SmtpSecurityMode mode, CancellationToken cancellationToken)
    {
        if (!delivery)
            return await new SmtpConnectionProbe().CheckAsync("127.0.0.1", port, mode, Username, Password, cancellationToken);
        var result = await new AccountSmtpEmailSender().SendAsync(
            new SmtpConfiguration { Host = "127.0.0.1", Port = port, SecurityMode = mode, Username = Username, FromEmail = Username },
            Password, new EmailOutboxMessage { Recipient = "recipient@example.test", Subject = "Loopback only", CreatedAtUtc = DateTime.UtcNow },
            new EmailOutboxPayload { Text = "Synthetic fixture" }, cancellationToken);
        return new SmtpSettingsResult(result.Accepted, result.ErrorCode);
    }

    private static void AssertNoCredentials(IEnumerable<string> commands, SmtpSettingsResult result)
    {
        var transcript = string.Join("\n", commands);
        Assert.DoesNotContain("AUTH", transcript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MAIL FROM", transcript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RCPT TO", transcript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DATA", transcript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Username, transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Password)), transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{Username}\0{Password}")), transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, result.Error ?? "", StringComparison.Ordinal);
    }

    private static StreamReader CreateReader(Stream stream) =>
        new(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

    private static async Task<string> ReadCommandAsync(StreamReader reader, List<string> commands, CancellationToken token)
    {
        var command = await reader.ReadLineAsync(token);
        Assert.NotNull(command);
        commands.Add(command);
        return command;
    }

    private static async Task DrainCommandsAsync(StreamReader reader, Stream stream,
        List<string> commands, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } command)
        {
            commands.Add(command);
            if (command.StartsWith("EHLO ", StringComparison.OrdinalIgnoreCase))
                await ReplyAsync(stream, "250-localhost\r\n250 AUTH PLAIN LOGIN\r\n", token);
            else if (command.StartsWith("AUTH ", StringComparison.OrdinalIgnoreCase))
                await ReplyAsync(stream, "535 Authentication rejected by test fixture\r\n", token);
            else if (command.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyAsync(stream, "221 Bye\r\n", token);
                return;
            }
            else
                await ReplyAsync(stream, "500 Unsupported test command\r\n", token);
        }
    }

    private static Task ReplyAsync(Stream stream, string response, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(response), token).AsTask();

    private static X509Certificate2 CreateUntrustedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = new(TestTimeout);
        private readonly TaskCompletionSource _accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Port { get; }
        public Task Completion { get; }
        public Task ConnectionAccepted => _accepted.Task;

        public LoopbackServer(Func<Stream, CancellationToken, Task> handler)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Completion = ServeAsync(handler);
        }

        private async Task ServeAsync(Func<Stream, CancellationToken, Task> handler)
        {
            using var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
            _accepted.TrySetResult();
            await handler(client.GetStream(), _lifetime.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _lifetime.CancelAsync();
            _listener.Stop();
            try
            {
                await Completion.WaitAsync(TestTimeout);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // Expected when cleanup cancels an outstanding accept/read or closes its socket.
            }
            finally
            {
                _lifetime.Dispose();
            }
        }
    }
}

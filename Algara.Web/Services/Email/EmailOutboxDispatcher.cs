using System.Data;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Services.Email;

public sealed class EmailOutboxDispatcher(IdentityDbContext db, ISmtpSecretProtector secrets,
    EmailOutboxFactory factory, IAccountEmailSender sender)
{
    /// <summary>Dispatches at most one leased message; false means no currently available work.</summary>
    public async Task<bool> DispatchOneAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // Terminal rows retain only safe delivery metadata. Expiry cleanup also works while SMTP is off.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [EmailOutboxMessages]
            SET [State] = 'Cancelled', [ProtectedPayload] = NULL, [LeaseId] = NULL,
                [LeaseUntilUtc] = NULL, [LastError] = 'expired'
            WHERE [State] IN ('Pending','Processing') AND [ExpiresAtUtc] <= {now};
            UPDATE [EmailOutboxMessages]
            SET [State] = 'Failed', [ProtectedPayload] = NULL, [LeaseId] = NULL,
                [LeaseUntilUtc] = NULL, [LastError] = 'retry_limit'
            WHERE [State] = 'Processing' AND [LeaseUntilUtc] <= {now} AND [Attempts] >= 3;
            """, cancellationToken);

        var settings = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (!EmailDeliveryReadiness.TryGetPassword(settings, secrets, out _)) return false;
        var message = await ClaimAsync(now, cancellationToken);
        if (message == null) return false;

        if (!await TokenIsCurrentAsync(message, cancellationToken))
        {
            await FinishAsync(message, EmailOutboxStates.Cancelled, "token_no_longer_valid", cancellationToken);
            return true;
        }
        if (!factory.TryReadPayload(message, out var payload))
        {
            await FinishAsync(message, EmailOutboxStates.Failed, "payload_unreadable", cancellationToken);
            return true;
        }
        // Read configuration again immediately before SMTP; never use a stale cached credential.
        settings = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (!EmailDeliveryReadiness.TryGetPassword(settings, secrets, out var password))
        {
            await ReleaseUnsentAsync(message, cancellationToken);
            return true;
        }

        // No database/access-management transaction is held during network I/O.
        var result = await sender.SendAsync(settings!, password, message, payload, cancellationToken);
        if (result.Accepted)
            await FinishAsync(message, EmailOutboxStates.Accepted, null, cancellationToken);
        else if (!result.Retryable || message.Attempts >= 3)
            await FinishAsync(message, EmailOutboxStates.Failed, SafeError(result.ErrorCode), cancellationToken);
        else
        {
            var next = DateTime.UtcNow.AddMinutes(message.Attempts == 1 ? 1 : 5);
            var safeError = SafeError(result.ErrorCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [EmailOutboxMessages]
                SET [State] = 'Pending', [LeaseId] = NULL, [LeaseUntilUtc] = NULL,
                    [NextAttemptAtUtc] = {next}, [LastError] = {safeError}
                WHERE [Id] = {message.Id} AND [State] = 'Processing' AND [LeaseId] = {message.LeaseId};
                """, cancellationToken);
        }
        return true;
    }

    private async Task<EmailOutboxMessage?> ClaimAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var message = await db.EmailOutboxMessages.FromSqlInterpolated($"""
            SELECT TOP(1) * FROM [EmailOutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE [Attempts] < 3 AND [NextAttemptAtUtc] <= {now}
                AND ([ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] > {now})
                AND ([State] = 'Pending' OR ([State] = 'Processing' AND [LeaseUntilUtc] <= {now}))
            ORDER BY [CreatedAtUtc], [Id]
            """).SingleOrDefaultAsync(cancellationToken);
        if (message == null) return null;
        message.State = EmailOutboxStates.Processing;
        message.LeaseId = Guid.NewGuid();
        message.LeaseUntilUtc = now.AddMinutes(2);
        message.Attempts++;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.Entry(message).State = EntityState.Detached;
        return message;
    }

    private async Task<bool> TokenIsCurrentAsync(EmailOutboxMessage message, CancellationToken cancellationToken)
    {
        if (!message.TokenId.HasValue) return true;
        var token = await db.AccountEmailTokens.AsNoTracking().SingleOrDefaultAsync(t => t.Id == message.TokenId, cancellationToken);
        if (token == null || token.RevokedAtUtc.HasValue || token.ConsumedAtUtc.HasValue || token.ExpiresAtUtc <= DateTime.UtcNow ||
            !string.Equals(token.Email, message.Recipient, StringComparison.OrdinalIgnoreCase)) return false;
        if (!token.UserN.HasValue) return true;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.N == token.UserN, cancellationToken);
        return user is { IsActive: true } && user.SecurityStamp == token.ExpectedSecurityStamp &&
            string.Equals(user.Email, token.Email, StringComparison.OrdinalIgnoreCase);
    }

    private Task<int> FinishAsync(EmailOutboxMessage message, string state, string? error, CancellationToken cancellationToken)
    {
        DateTime? accepted = state == EmailOutboxStates.Accepted ? DateTime.UtcNow : null;
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [EmailOutboxMessages]
            SET [State] = {state}, [AcceptedAtUtc] = {accepted}, [LastError] = {error},
                [ProtectedPayload] = NULL, [LeaseId] = NULL, [LeaseUntilUtc] = NULL
            WHERE [Id] = {message.Id} AND [State] = 'Processing' AND [LeaseId] = {message.LeaseId};
            """, cancellationToken);
    }

    private Task<int> ReleaseUnsentAsync(EmailOutboxMessage message, CancellationToken cancellationToken)
    {
        var next = DateTime.UtcNow.AddMinutes(1);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [EmailOutboxMessages]
            SET [State] = 'Pending', [Attempts] = [Attempts] - 1, [LeaseId] = NULL, [LeaseUntilUtc] = NULL,
                [NextAttemptAtUtc] = {next}, [LastError] = 'smtp_unavailable'
            WHERE [Id] = {message.Id} AND [State] = 'Processing' AND [LeaseId] = {message.LeaseId};
            """, cancellationToken);
    }

    private static string SafeError(string? code) => code switch
    {
        "smtp_tls_required" or "smtp_authentication_failed" or "smtp_tls_failed" or "smtp_timeout" or
        "smtp_rejected" or "smtp_connection_failed" or "invalid_message" => code,
        _ => "smtp_failed"
    };
}

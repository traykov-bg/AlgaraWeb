using Algara.Identity.Models;

namespace Algara.Web.Services.Email;

public sealed class SmtpSettingsSnapshot
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public SmtpSecurityMode SecurityMode { get; init; }
    public string Username { get; init; } = string.Empty;
    public string FromEmail { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public string? PublicSiteUrl { get; init; }
    public bool Enabled { get; init; }
    public string Version { get; init; } = "none";
    public bool HasPassword { get; init; }
    public bool PasswordReadable { get; init; }
    public IReadOnlyList<SmtpSettingsHistoryEntry> History { get; init; } = [];
}

// A class deliberately has no generated ToString containing the write-only password.
public sealed class SmtpSettingsUpdate
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public SmtpSecurityMode SecurityMode { get; set; } = SmtpSecurityMode.StartTls;
    public string Username { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string? PublicSiteUrl { get; set; }
    public bool Enabled { get; set; }
    public string? ExpectedVersion { get; set; }
    public string? Password { get; set; }
    public bool RemovePassword { get; set; }
}

public sealed record SmtpSettingsHistoryEntry(string ActorName, DateTime OccurredAtUtc, string Description);
public sealed record SmtpSettingsResult(bool Success, string? Error = null, bool Changed = false);

public interface ISmtpSecretProtector
{
    string Protect(string password);
    bool TryUnprotect(string protectedPassword, out string password);
}

public interface ISmtpConnectionProbe
{
    Task<SmtpSettingsResult> CheckAsync(string host, int port, SmtpSecurityMode securityMode,
        string username, string password, CancellationToken cancellationToken);
}

namespace Algara.Identity.Models;

public enum SmtpSecurityMode
{
    StartTls = 1,
    ImplicitTls = 2
}

/// <summary>Single administrative mail configuration; only protected credentials may be stored.</summary>
public sealed class SmtpConfiguration
{
    public int Id { get; set; } = 1;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public SmtpSecurityMode SecurityMode { get; set; } = SmtpSecurityMode.StartTls;
    public string Username { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string PublicSiteUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? ProtectedPassword { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

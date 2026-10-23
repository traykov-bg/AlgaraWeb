using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Services.Email;

public sealed class EmailDeliveryReadiness(IdentityDbContext db, ISmtpSecretProtector secrets)
{
    public async Task<string?> GetPublicSiteUrlAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, cancellationToken);
        return TryGetPassword(settings, secrets, out _) &&
            SmtpSettingsRules.TryPublicSiteUrl(settings!.PublicSiteUrl, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority) : null;
    }

    public static bool TryGetPassword(SmtpConfiguration? settings, ISmtpSecretProtector secrets, out string password)
    {
        password = string.Empty;
        if (settings is not { Enabled: true } || string.IsNullOrEmpty(settings.ProtectedPassword)) return false;
        var check = new SmtpSettingsUpdate
        {
            Host = settings.Host, Port = settings.Port, SecurityMode = settings.SecurityMode,
            Username = settings.Username, FromEmail = settings.FromEmail, FromName = settings.FromName,
            ExpectedVersion = "none"
        };
        return SmtpSettingsRules.Validate(check) == null && secrets.TryUnprotect(settings.ProtectedPassword, out password);
    }
}

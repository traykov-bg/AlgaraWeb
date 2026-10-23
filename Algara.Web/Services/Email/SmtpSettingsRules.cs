using System.Net.Mail;
using Algara.Identity.Models;

namespace Algara.Web.Services.Email;

public static class SmtpSettingsRules
{
    public static string Version(SmtpConfiguration? settings) =>
        settings == null ? "none" : Convert.ToBase64String(settings.RowVersion);

    public static bool IsVersion(string? value)
    {
        if (value == "none") return true;
        if (value?.Length != 12) return false;
        Span<byte> bytes = stackalloc byte[8];
        return Convert.TryFromBase64String(value, bytes, out var length) && length == 8 &&
            Convert.ToBase64String(bytes) == value;
    }

    public static string? Validate(SmtpSettingsUpdate update)
    {
        update.Host = (update.Host ?? string.Empty).Trim().ToLowerInvariant();
        update.Username = (update.Username ?? string.Empty).Trim();
        update.FromEmail = (update.FromEmail ?? string.Empty).Trim();
        update.FromName = (update.FromName ?? string.Empty).Trim();
        // The optional address is stored as empty text in the existing NOT NULL column.
        update.PublicSiteUrl = (update.PublicSiteUrl ?? string.Empty).Trim();
        if (!IsVersion(update.ExpectedVersion))
            return "Липсва или е невалидна версията на настройките. Презаредете страницата.";
        if (update.Host.Length is 0 or > 253 || HasControls(update.Host) ||
            Uri.CheckHostName(update.Host) != UriHostNameType.Dns ||
            update.Host.Contains(':') || update.Host.Contains('/') || update.Host.EndsWith('.'))
            return "Въведете валидно име на SMTP сървър без протокол, път или порт.";
        if (update.Port is < 1 or > 65535)
            return "Портът трябва да е между 1 и 65535.";
        if (update.SecurityMode is not (SmtpSecurityMode.StartTls or SmtpSecurityMode.ImplicitTls))
            return "Изберете STARTTLS или TLS от началото на връзката.";
        if ((update.Port == 465 && update.SecurityMode != SmtpSecurityMode.ImplicitTls) ||
            (update.Port == 587 && update.SecurityMode != SmtpSecurityMode.StartTls))
            return "Порт 587 използва STARTTLS, а порт 465 — TLS от началото на връзката.";
        if (update.Username.Length is 0 or > 256 || HasControls(update.Username))
            return "Въведете потребителско име до 256 знака. Удостоверяването е задължително.";
        if (update.FromEmail.Length is 0 or > 256 || HasControls(update.FromEmail) ||
            !MailAddress.TryCreate(update.FromEmail, out var address) ||
            !string.Equals(address.Address, update.FromEmail, StringComparison.Ordinal))
            return "Въведете един валиден имейл адрес на подателя.";
        if (update.FromName.Length > 200 || HasControls(update.FromName))
            return "Името на подателя трябва да е до 200 знака и на един ред.";
        if (update.PublicSiteUrl.Length > 0 && !TryPublicSiteUrl(update.PublicSiteUrl, out _))
            return "Въведете HTTPS адреса на сайта без път, потребителско име, параметри или фрагмент. HTTP е допустим само за localhost.";
        if (update.Password?.Length > 1024 || (update.Password != null && HasControls(update.Password)))
            return "Въведете валидна SMTP парола до 1024 знака.";
        if (string.IsNullOrWhiteSpace(update.Password)) update.Password = null;
        if (update.RemovePassword && update.Password != null)
            return "Изберете или нова парола, или премахване на записаната парола.";
        if (update.RemovePassword && update.Enabled)
            return "Изключете изпращането, преди да премахнете паролата.";
        if (update.Host == "smtp.gmail.com" && update.Password != null)
        {
            update.Password = update.Password.Replace(" ", string.Empty);
            if (update.Password.Length != 16 || !update.Password.All(char.IsAsciiLetterOrDigit))
                return "За Gmail въведете 16-знакова парола за приложение (App Password), създадена в Google профила.";
        }
        return null;
    }

    public static bool EndpointChanged(SmtpConfiguration current, SmtpSettingsUpdate update) =>
        !string.Equals(current.Host, update.Host, StringComparison.OrdinalIgnoreCase) ||
        current.Port != update.Port || current.SecurityMode != update.SecurityMode ||
        !string.Equals(current.Username, update.Username, StringComparison.Ordinal);

    public static bool TryPublicSiteUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || HasControls(value) ||
            value.Contains('\\') || !Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            candidate.UserInfo.Length != 0 || candidate.Query.Length != 0 || candidate.Fragment.Length != 0 ||
            candidate.AbsolutePath != "/" || candidate.Host.Length == 0)
            return false;
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0) return false;
        var authority = value[(authorityStart + 3)..].TrimEnd('/');
        var rawHost = authority.StartsWith('[') ? authority[..(authority.IndexOf(']') + 1)] : authority.Split(':')[0];
        var isLocal = rawHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            rawHost is "127.0.0.1" or "[::1]";
        if (candidate.Scheme != Uri.UriSchemeHttps && !(candidate.Scheme == Uri.UriSchemeHttp && isLocal))
            return false;
        uri = candidate;
        return true;
    }

    private static bool HasControls(string value) => value.Any(char.IsControl);
}

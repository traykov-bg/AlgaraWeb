using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services.Email;

public sealed class SmtpSettingsService(IdentityDbContext db, ISmtpSecretProtector secrets,
    ISmtpConnectionProbe probe, IConfiguration configuration)
{
    public async Task<SmtpSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var saved = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, cancellationToken);
        var settings = saved ?? Defaults();
        var hasPassword = !string.IsNullOrEmpty(settings.ProtectedPassword);
        var readable = hasPassword && secrets.TryUnprotect(settings.ProtectedPassword!, out _);
        var history = await db.SmtpSettingsAuditEvents.AsNoTracking().OrderByDescending(e => e.Id)
            .Take(20).Select(e => new { e.ActorName, e.OccurredAtUtc, e.BeforeJson, e.AfterJson }).ToListAsync(cancellationToken);
        return new()
        {
            Host = settings.Host, Port = settings.Port, SecurityMode = settings.SecurityMode,
            Username = settings.Username, FromEmail = settings.FromEmail, FromName = settings.FromName,
            PublicSiteUrl = settings.PublicSiteUrl,
            Enabled = settings.Enabled, Version = SmtpSettingsRules.Version(saved),
            HasPassword = hasPassword, PasswordReadable = readable,
            History = history.Select(e => new SmtpSettingsHistoryEntry(e.ActorName, e.OccurredAtUtc,
                Describe(e.BeforeJson, e.AfterJson))).ToList()
        };
    }

    public async Task<SmtpSettingsResult> SaveAsync(ClaimsPrincipal actor, SmtpSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var error = SmtpSettingsRules.Validate(update);
            if (error != null) return new(false, error);
            await using var transaction = await AcquireAsync(cancellationToken);
            var staff = await FindAdminAsync(actor, cancellationToken);
            if (staff == null) return new(false, "Само Администратор с актуална сесия може да променя SMTP настройките.");
            var current = await db.SmtpConfigurations.SingleOrDefaultAsync(s => s.Id == 1, cancellationToken);
            if (current != null) await db.Entry(current).ReloadAsync(cancellationToken);
            if (SmtpSettingsRules.Version(current) != update.ExpectedVersion)
                return new(false, "Настройките са променени. Презаредете страницата и проверете стойностите.");
            if (current?.ProtectedPassword != null && SmtpSettingsRules.EndpointChanged(current, update) &&
                update.Password == null && !update.RemovePassword)
                return new(false, "При промяна на SMTP сървъра, порта, защитата или потребителското име въведете паролата отново или я премахнете.");

            var protectedPassword = update.RemovePassword ? null : current?.ProtectedPassword;
            if (update.Password != null) protectedPassword = secrets.Protect(update.Password);
            if (update.Enabled && (string.IsNullOrEmpty(protectedPassword) || !secrets.TryUnprotect(protectedPassword, out _)))
                return new(false, "За включване на изпращането въведете валидна SMTP парола. Записаната парола липсва или не може да бъде прочетена.");
            var before = current == null ? null : Redacted(current);
            var passwordChanged = update.Password != null || (update.RemovePassword && current?.ProtectedPassword != null);
            var changed = current == null || SmtpSettingsRules.EndpointChanged(current, update) ||
                current.FromEmail != update.FromEmail || current.FromName != update.FromName ||
                current.PublicSiteUrl != update.PublicSiteUrl ||
                current.Enabled != update.Enabled || passwordChanged;
            if (!changed) return new(true);
            current ??= new SmtpConfiguration();
            if (db.Entry(current).State == EntityState.Detached) db.SmtpConfigurations.Add(current);
            current.Host = update.Host;
            current.Port = update.Port;
            current.SecurityMode = update.SecurityMode;
            current.Username = update.Username;
            current.FromEmail = update.FromEmail;
            current.FromName = update.FromName;
            current.PublicSiteUrl = update.PublicSiteUrl ?? string.Empty;
            current.Enabled = update.Enabled;
            current.ProtectedPassword = protectedPassword;
            db.SmtpSettingsAuditEvents.Add(new()
            {
                ActorId = staff.Id,
                ActorName = ActorName(staff),
                OccurredAtUtc = DateTime.UtcNow,
                Action = "SettingsSaved",
                BeforeJson = JsonSerializer.Serialize(before),
                AfterJson = JsonSerializer.Serialize(Redacted(current, passwordChanged))
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, Changed: true);
        }
        catch (CryptographicException)
        {
            db.ChangeTracker.Clear();
            return new(false, "Паролата не може да бъде защитена. Настройките не са записани.");
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new(false, "Настройките и историята не са записани. Презаредете страницата и опитайте отново.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004)
        {
            db.ChangeTracker.Clear();
            return new(false, "Настройките се обработват от друго действие. Презаредете страницата.");
        }
        finally
        {
            update.Password = null;
        }
    }

    public async Task<SmtpSettingsResult> CheckConnectionAsync(ClaimsPrincipal actor, string? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (expectedVersion == "none" || !SmtpSettingsRules.IsVersion(expectedVersion))
            return new(false, "Първо запишете SMTP настройките и паролата.");
        try
        {
            // Keep the access lock until the bounded probe completes, so a role/settings change
            // cannot race an outbound authentication using an older authorization snapshot.
            await using var transaction = await AcquireAsync(cancellationToken);
            if (await FindAdminAsync(actor, cancellationToken) == null)
                return new(false, "Само Администратор с актуална сесия може да проверява SMTP връзката.");
            var settings = await db.SmtpConfigurations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, cancellationToken);
            if (settings == null || SmtpSettingsRules.Version(settings) != expectedVersion)
                return new(false, "Настройките са променени. Презаредете страницата преди проверката.");
            var check = new SmtpSettingsUpdate
            {
                Host = settings.Host, Port = settings.Port, SecurityMode = settings.SecurityMode,
                Username = settings.Username, FromEmail = settings.FromEmail, FromName = settings.FromName,
                ExpectedVersion = expectedVersion, Enabled = settings.Enabled
            };
            if (SmtpSettingsRules.Validate(check) != null)
                return new(false, "Записаните SMTP настройки са невалидни. Запишете ги отново.");
            if (string.IsNullOrEmpty(settings.ProtectedPassword) || !secrets.TryUnprotect(settings.ProtectedPassword, out var password))
                return new(false, "Записаната парола липсва или не може да бъде прочетена. Въведете я отново.");
            return await probe.CheckAsync(settings.Host, settings.Port, settings.SecurityMode,
                settings.Username, password, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004)
        {
            return new(false, "Настройките се обработват от друго действие. Опитайте отново.");
        }
    }

    private async Task<ApplicationUser?> FindAdminAsync(ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        // A new service forces a live database check instead of the earlier MVC filter snapshot.
        if (!await new StaffAccessService(db).HasPermissionAsync(actor, PermissionCatalog.Settings.Manage, cancellationToken))
            return null;
        var actorId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == actorId, cancellationToken);
    }

    private async Task<IDbContextTransaction> AcquireAsync(CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=N'Algara.AccessManagement', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51004, 'Access management is busy.', 1;
                """, cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private SmtpConfiguration Defaults() => new()
    {
        Host = configuration["Email:Defaults:Host"] ?? "smtp.gmail.com",
        Port = configuration.GetValue("Email:Defaults:Port", 587),
        SecurityMode = SmtpSecurityMode.StartTls,
        Username = configuration["Email:Defaults:Username"] ?? string.Empty,
        FromEmail = configuration["Email:Defaults:FromEmail"] ?? string.Empty,
        FromName = configuration["Email:Defaults:FromName"] ?? "Алгара",
        Enabled = false
    };

    private static string ActorName(ApplicationUser staff)
    {
        var name = string.IsNullOrWhiteSpace(staff.DisplayName) ? staff.UserName : staff.DisplayName;
        return name.Length <= 256 ? name : name[..256];
    }

    // An explicit allowlist prevents credentials (even ciphertext) entering history.
    private static object Redacted(SmtpConfiguration settings, bool passwordChanged = false) => new
    {
        settings.Host, settings.Port, SecurityMode = settings.SecurityMode.ToString(), settings.Username,
        settings.FromEmail, settings.FromName, settings.Enabled,
        settings.PublicSiteUrl,
        HasPassword = !string.IsNullOrEmpty(settings.ProtectedPassword), PasswordChanged = passwordChanged
    };

    private static string Describe(string beforeJson, string afterJson)
    {
        try
        {
            using var before = JsonDocument.Parse(beforeJson);
            using var after = JsonDocument.Parse(afterJson);
            if (after.RootElement.ValueKind != JsonValueKind.Object) return "Промяна на SMTP настройки.";
            var labels = new Dictionary<string, string>
            {
                ["Host"] = "сървър", ["Port"] = "порт", ["SecurityMode"] = "TLS режим",
                ["Username"] = "потребителско име", ["FromEmail"] = "имейл на подателя",
                ["FromName"] = "име на подателя", ["Enabled"] = "включване на изпращането",
                ["PublicSiteUrl"] = "публичен адрес на сайта"
            };
            var fields = labels.Where(item => after.RootElement.TryGetProperty(item.Key, out var next) &&
                (before.RootElement.ValueKind != JsonValueKind.Object || !before.RootElement.TryGetProperty(item.Key, out var old) ||
                next.GetRawText() != old.GetRawText())).Select(item => item.Value).ToList();
            if (after.RootElement.TryGetProperty("PasswordChanged", out var passwordChanged) && passwordChanged.ValueKind == JsonValueKind.True)
                fields.Add("парола (стойността не се показва)");
            return (before.RootElement.ValueKind == JsonValueKind.Null ? "Първоначално записани: " : "Променени: ") +
                string.Join(", ", fields) + ".";
        }
        catch (JsonException) { return "Промяна на SMTP настройки."; }
    }
}

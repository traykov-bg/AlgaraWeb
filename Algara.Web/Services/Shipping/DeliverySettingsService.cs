using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Web.Services.Shipping.Carriers;
using Algara.Web.ViewModels.Admin;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Services.Shipping;

public interface IDeliverySettingsReader
{
    Task<CourierConnection?> GetCourierAsync(CourierProvider provider, CancellationToken ct = default);
    Task<TransportQuoteSettings?> GetTransportAsync(CancellationToken ct = default);
}

public sealed class TransportQuoteSettings(decimal latitude, decimal longitude, string apiKey)
{
    public decimal Latitude { get; } = latitude;
    public decimal Longitude { get; } = longitude;
    public string ApiKey { get; } = apiKey;
    public override string ToString() => "Transport quote settings";
}

public sealed class DeliverySettingsService(IdentityDbContext db, DeliverySecretProtector secrets) : IDeliverySettingsReader
{
    public const string OriginAddress = "кв. Тракия, бул. „Освобождение“ 39, 4023 Пловдив, България";
    public static string Version(byte[]? version) => version is { Length: > 0 } ? Convert.ToBase64String(version) : "none";

    public async Task<DeliverySettingsForm> GetFormAsync(CancellationToken ct = default)
    {
        var couriers = await db.CourierConfigurations.AsNoTracking().ToListAsync(ct);
        var origin = await db.TransportConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        var events = await db.AccessAuditEvents.AsNoTracking().Where(x => x.Action == "DeliverySettingsSaved")
            .OrderByDescending(x => x.Id).Take(10).Select(x => new { x.ActorName, x.OccurredAtUtc }).ToListAsync(ct);
        return new()
        {
            Econt = ToForm(couriers.SingleOrDefault(x => x.Id == 1)),
            Speedy = ToForm(couriers.SingleOrDefault(x => x.Id == 2)),
            Transport = new()
            {
                ExpectedVersion = Version(origin?.RowVersion), OriginLatitude = origin?.OriginLatitude,
                OriginLongitude = origin?.OriginLongitude, GeocodingEnabled = origin?.GeocodingEnabled ?? false,
                HasReadableKey = secrets.TryUnprotect(origin?.ProtectedGeocodingKey, out _)
            },
            History = events.Select(x => $"{x.OccurredAtUtc:u} — {x.ActorName}").ToArray()
        };
    }

    public async Task<CourierConnection?> GetCourierAsync(CourierProvider provider, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(provider)) return null;
        var saved = await db.CourierConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == (int)provider, ct);
        if (saved is not { Enabled: true } || IsPublicDemoInProduction(saved.Id, saved.UseSandbox, saved.UserName) ||
            IsApiAddress(saved.UserName) || !secrets.TryUnprotect(saved.ProtectedPassword, out var password)) return null;
        var connection = Connection(saved, password);
        return CourierGateway.IsConfigured(connection) ? connection : null;
    }

    public async Task<TransportQuoteSettings?> GetTransportAsync(CancellationToken ct = default)
    {
        var saved = await db.TransportConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        return saved is { GeocodingEnabled: true, OriginLatitude: not null, OriginLongitude: not null } &&
            secrets.TryUnprotect(saved.ProtectedGeocodingKey, out var key)
            ? new(saved.OriginLatitude.Value, saved.OriginLongitude.Value, key) : null;
    }

    public async Task<string?> SaveAsync(ClaimsPrincipal actor, DeliverySettingsForm form, CancellationToken ct = default)
    {
        try
        {
            var error = Validate(form);
            if (error != null) return error;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @r int;
                EXEC @r = sys.sp_getapplock @Resource=N'Algara.AccessManagement', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @r < 0 THROW 51004, 'Access management is busy.', 1;
                """, ct);
            if (!await new StaffAccessService(db).HasPermissionAsync(actor, PermissionCatalog.Settings.Manage, ct))
                return "Само Администратор с актуална сесия може да променя настройките.";
            var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
            var staff = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
            var existing = await db.CourierConfigurations.ToListAsync(ct);
            foreach (var row in existing) await db.Entry(row).ReloadAsync(ct);
            var origin = await db.TransportConfigurations.SingleOrDefaultAsync(x => x.Id == 1, ct);
            if (origin != null) await db.Entry(origin).ReloadAsync(ct);
            var before = Audit(existing, origin);
            var pending = new List<CourierConfiguration>();
            foreach (var (id, entry) in new[] { (1, form.Econt), (2, form.Speedy) })
            {
                var saved = existing.SingleOrDefault(x => x.Id == id);
                if (Version(saved?.RowVersion) != entry.ExpectedVersion) return Stale;
                var credentialError = ValidateCredentialTransition(saved, entry);
                if (credentialError != null) return credentialError;
                var protectedPassword = entry.RemovePassword ? null : saved?.ProtectedPassword;
                if (!string.IsNullOrEmpty(entry.Password)) protectedPassword = secrets.Protect(entry.Password);
                var next = new CourierConfiguration
                {
                    Id = id, Enabled = entry.Enabled, UseSandbox = entry.UseSandbox,
                    UserName = Clean(entry.UserName), ProtectedPassword = protectedPassword,
                    SenderClientNumber = Clean(entry.SenderClientNumber), SenderOfficeCode = Clean(entry.SenderOfficeCode),
                    SenderName = Clean(entry.SenderName), SenderPhone = Clean(entry.SenderPhone), ServiceId = entry.ServiceId
                };
                if (next.Enabled && (!secrets.TryUnprotect(protectedPassword, out var plain) ||
                    !CourierGateway.IsConfigured(Connection(next, plain))))
                    return "За включване на куриер попълнете валиден API профил, парола и данни за подателя и услугата.";
                pending.Add(next);
            }
            if (Version(origin?.RowVersion) != form.Transport.ExpectedVersion) return Stale;
            var protectedKey = form.Transport.RemoveGeocodingKey ? null : origin?.ProtectedGeocodingKey;
            if (!string.IsNullOrEmpty(form.Transport.GeocodingKey)) protectedKey = secrets.Protect(form.Transport.GeocodingKey);
            if (form.Transport.GeocodingEnabled && (!secrets.TryUnprotect(protectedKey, out _) ||
                form.Transport.OriginLatitude == null || form.Transport.OriginLongitude == null))
                return "За автоматичен собствен транспорт въведете потвърдени координати на отправната точка и Google Geocoding API ключ.";
            foreach (var next in pending)
            {
                var saved = existing.SingleOrDefault(x => x.Id == next.Id);
                if (saved == null) db.CourierConfigurations.Add(next);
                else
                {
                    var token = saved.RowVersion;
                    db.Entry(saved).CurrentValues.SetValues(next);
                    saved.RowVersion = token;
                }
            }
            origin ??= new TransportConfiguration();
            if (db.Entry(origin).State == EntityState.Detached) db.TransportConfigurations.Add(origin);
            origin.OriginLatitude = form.Transport.OriginLatitude;
            origin.OriginLongitude = form.Transport.OriginLongitude;
            origin.GeocodingEnabled = form.Transport.GeocodingEnabled;
            origin.ProtectedGeocodingKey = protectedKey;
            var actorName = string.IsNullOrWhiteSpace(staff.DisplayName) ? staff.UserName : staff.DisplayName;
            db.AccessAuditEvents.Add(new()
            {
                ActorId = staff.Id, ActorName = actorName[..Math.Min(actorName.Length, 256)],
                Action = "DeliverySettingsSaved", Target = "Delivery", TargetName = "Доставка",
                BeforeJson = before, AfterJson = Audit(pending, origin), OccurredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        catch (CryptographicException) { db.ChangeTracker.Clear(); return "Защитата на ключовете не е достъпна. Настройките не са записани."; }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return Stale; }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004)
        { db.ChangeTracker.Clear(); return "Настройките се обработват от друго действие. Опитайте отново."; }
        finally
        {
            if (form.Econt != null) form.Econt.Password = null;
            if (form.Speedy != null) form.Speedy.Password = null;
            if (form.Transport != null) form.Transport.GeocodingKey = null;
        }
    }

    public static string? Validate(DeliverySettingsForm form)
    {
        if (form.Econt == null || form.Speedy == null || form.Transport == null)
            return "Липсва част от настройките за доставка. Презаредете страницата.";
        foreach (var item in new object[] { form, form.Econt, form.Speedy, form.Transport })
            if (!Validator.TryValidateObject(item, new ValidationContext(item), [], true)) return "Проверете попълнените настройки и дължината на полетата.";
        foreach (var item in new[] { form.Econt, form.Speedy })
        {
            if (!ValidVersion(item.ExpectedVersion)) return Stale;
            if (IsApiAddress(item.UserName))
                return "В полето „API потребител“ въведете потребителското име на куриерския профил, а не интернет адреса на API услугата. Адресът се избира автоматично.";
            if (item.RemovePassword && !string.IsNullOrEmpty(item.Password)) return "Изберете въвеждане или премахване на паролата.";
            if (!string.IsNullOrEmpty(item.Password) && (string.IsNullOrWhiteSpace(item.Password) || item.Password.Any(char.IsControl)))
                return "Въведете валидна API парола без контролни знаци.";
        }
        if (form.Speedy.UseSandbox) return "Тестова среда е налична само за Econt. За Speedy използвайте предоставения API профил.";
        if (form.Econt.Enabled && IsPublicDemoInProduction(1, form.Econt.UseSandbox, form.Econt.UserName))
            return "Публичният тестов API потребител iasp-dev работи само в тестовата среда на Econt. Изберете „Тестова среда“ или въведете реалния си API профил.";
        var origin = form.Transport;
        if (!ValidVersion(origin.ExpectedVersion)) return Stale;
        if (origin.OriginLatitude.HasValue != origin.OriginLongitude.HasValue) return "Попълнете и двете координати на отправната точка.";
        if (origin.OriginLatitude is { } lat && decimal.Round(lat, 7) != lat ||
            origin.OriginLongitude is { } lon && decimal.Round(lon, 7) != lon) return "Координатите позволяват до 7 десетични знака.";
        if (origin.RemoveGeocodingKey && !string.IsNullOrEmpty(origin.GeocodingKey)) return "Изберете въвеждане или премахване на API ключа.";
        if (!string.IsNullOrEmpty(origin.GeocodingKey) && origin.GeocodingKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return "Въведете API ключ без интервали или нови редове.";
        return null;
    }

    public static string? ValidateCredentialTransition(CourierConfiguration? saved, CourierSettingsForm entry)
    {
        if (saved?.ProtectedPassword != null &&
            (saved.UserName != Clean(entry.UserName) || saved.UseSandbox != entry.UseSandbox) &&
            string.IsNullOrEmpty(entry.Password) && !entry.RemovePassword)
            return "При промяна на API потребителя или средата въведете паролата отново или я премахнете. Запазената парола не се прехвърля автоматично към друг профил или среда.";
        return null;
    }

    private static bool ValidVersion(string? value)
    {
        if (value == "none") return true;
        Span<byte> buffer = stackalloc byte[8];
        return value != null && Convert.TryFromBase64String(value, buffer, out var size) && size == 8 &&
            Convert.ToBase64String(buffer[..size]) == value;
    }
    private const string Stale = "Настройките са променени или версията е невалидна. Презаредете страницата.";
    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
    private static bool IsApiAddress(string? userName) => Clean(userName).IndexOfAny(['/', '\\']) >= 0;
    private static bool IsPublicDemoInProduction(int provider, bool useSandbox, string? userName) =>
        provider == (int)CourierProvider.Econt && !useSandbox &&
        string.Equals(Clean(userName), "iasp-dev", StringComparison.OrdinalIgnoreCase);
    private CourierSettingsForm ToForm(CourierConfiguration? saved) => new()
    {
        ExpectedVersion = Version(saved?.RowVersion), Enabled = saved?.Enabled ?? false,
        UseSandbox = saved?.UseSandbox ?? false, UserName = saved?.UserName,
        SenderClientNumber = saved?.SenderClientNumber, SenderOfficeCode = saved?.SenderOfficeCode,
        SenderName = saved?.SenderName, SenderPhone = saved?.SenderPhone, ServiceId = saved?.ServiceId,
        HasReadablePassword = secrets.TryUnprotect(saved?.ProtectedPassword, out _)
    };
    private static CourierConnection Connection(CourierConfiguration row, string password) => new()
    {
        Provider = (CourierProvider)row.Id, UseSandbox = row.UseSandbox, UserName = row.UserName, Password = password,
        SenderClientNumber = row.SenderClientNumber, SenderOfficeCode = row.SenderOfficeCode,
        SenderName = row.SenderName, SenderPhone = row.SenderPhone, ServiceId = row.ServiceId
    };
    // Credentials and ciphertext are deliberately absent, including from configuration history.
    private static string Audit(IEnumerable<CourierConfiguration> rows, TransportConfiguration? origin) => JsonSerializer.Serialize(new
    {
        Couriers = rows.Select(x => new { x.Id, x.Enabled, x.UseSandbox, x.SenderClientNumber, x.SenderOfficeCode, x.SenderName, x.SenderPhone, x.ServiceId, HasPassword = x.ProtectedPassword != null }),
        Origin = origin == null ? null : new { origin.OriginLatitude, origin.OriginLongitude, origin.GeocodingEnabled, HasKey = origin.ProtectedGeocodingKey != null }
    });
}

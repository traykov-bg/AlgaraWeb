using System.Globalization;
using System.Security.Claims;
using Algara.Identity.Data;
using Microsoft.EntityFrameworkCore;
namespace Algara.Web.Services;

public sealed class ClientTimeDisplay(IdentityDbContext db, IHttpContextAccessor context)
{
    private Task<(TimeZoneInfo? Zone, int? Offset)>? settings;
    private async Task<(TimeZoneInfo?, int?)> LoadAsync()
    {
        var actor = context.HttpContext?.User;
        if (actor?.Identity?.IsAuthenticated != true) return (null, null);
        var id = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        var session = actor.FindFirstValue("SessionId");
        var user = await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive)
            .Select(x => new { x.N, x.PreferredTimeZoneId }).SingleOrDefaultAsync();
        if (user == null) return (null, null);
        if (!string.IsNullOrWhiteSpace(user.PreferredTimeZoneId))
        {
            try { return (TimeZoneInfo.FindSystemTimeZoneById(user.PreferredTimeZoneId), null); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        var offset = await db.UserSessions.AsNoTracking().Where(x => x.UserN == user.N && x.SessionId == session && x.IsActive)
            .Select(x => x.TimeZoneOffset).SingleOrDefaultAsync();
        return (null, offset is >= -840 and <= 840 ? offset : null);
    }
    public async Task<(string Text, string? Zone)> FormatAsync(DateTime utc)
    {
        var (zone, offset) = await (settings ??= LoadAsync());
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = zone != null ? TimeZoneInfo.ConvertTimeFromUtc(utc, zone) : utc.AddMinutes(offset ?? 0);
        string? zoneId = zone?.Id;
        if (zoneId != null && TimeZoneInfo.TryConvertWindowsIdToIanaId(zoneId, out var iana)) zoneId = iana;
        return (local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + (zone == null && offset == null ? " UTC" : ""), zoneId);
    }
}

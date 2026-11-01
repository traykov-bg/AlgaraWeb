using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.Web.Services.Cart;

/// <summary>Only product identities and quantities are stored; prices are always read from the catalog.</summary>
public sealed class ProtectedCookieCartStore : ICartStore
{
    public const string CookieName = "Algara.Cart.v1";
    public const int MaxCookieLength = 3800;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private readonly IHttpContextAccessor contextAccessor;
    private readonly ITimeLimitedDataProtector protector;

    public ProtectedCookieCartStore(IHttpContextAccessor contextAccessor, IDataProtectionProvider provider)
    {
        this.contextAccessor = contextAccessor;
        protector = provider.CreateProtector("Algara.Cart", "v1").ToTimeLimitedDataProtector();
    }

    public IReadOnlyList<CartEntry> Read()
    {
        var value = contextAccessor.HttpContext?.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(value) || value.Length > MaxCookieLength)
            return [];
        try
        {
            var entries = JsonSerializer.Deserialize<List<CartEntry>>(protector.Unprotect(value));
            return IsValid(entries) ? entries! : [];
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return [];
        }
    }

    public void Write(IReadOnlyList<CartEntry> entries)
    {
        if (!IsValid(entries)) throw new ArgumentException("Invalid cart contents.", nameof(entries));
        var context = contextAccessor.HttpContext ?? throw new InvalidOperationException("An HTTP request is required.");
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/",
            MaxAge = Lifetime
        };
        if (entries.Count == 0)
        {
            context.Response.Cookies.Delete(CookieName, options);
            return;
        }
        var value = protector.Protect(JsonSerializer.Serialize(entries), Lifetime);
        if (value.Length > MaxCookieLength) throw new ArgumentException("Cart exceeds cookie capacity.", nameof(entries));
        context.Response.Cookies.Append(CookieName, value, options);
    }

    public static bool IsValid(IReadOnlyList<CartEntry>? entries)
        => entries != null && entries.Count <= CartService.MaxLines &&
            entries.All(entry => entry != null && entry.ProductN > 0 &&
                (entry.VariantN == null || entry.VariantN > 0) && entry.Quantity is >= 1 and <= CartService.MaxQuantity) &&
            entries.Select(entry => (entry.ProductN, entry.VariantN)).Distinct().Count() == entries.Count;
}

using Algara.Web.Services.Cart;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Algara.UnitTests.Services;

public class ProtectedCookieCartStoreTests
{
    [Fact]
    public void CookieRoundTrip_StoresOnlyProtectedIdentitiesAndQuantities()
    {
        var provider = new EphemeralDataProtectionProvider();
        var context = new DefaultHttpContext(); context.Request.Scheme = "https";
        var store = Store(context, provider);
        store.Write([new(12, 30, 2), new(13, null, 1)]);
        var header = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=2592000", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProductN", header);
        var request = new DefaultHttpContext();
        request.Request.Headers.Cookie = header.Split(';')[0];
        Assert.Equal(new[] { new CartEntry(12, 30, 2), new CartEntry(13, null, 1) }, Store(request, provider).Read());
        Assert.False(request.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public void CookieAtMaximumCartSizeFitsOneBrowserCookie()
    {
        var context = new DefaultHttpContext();
        var provider = new EphemeralDataProtectionProvider();
        var entries = Enumerable.Range(0, CartService.MaxLines)
            .Select(index => new CartEntry(int.MaxValue - index, int.MaxValue, CartService.MaxQuantity)).ToArray();
        Store(context, provider).Write(entries);
        var value = context.Response.Headers.SetCookie.ToString().Split(';')[0];
        Assert.True(value.Length < 4096);
        var request = new DefaultHttpContext(); request.Request.Headers.Cookie = value;
        Assert.Equal(entries, Store(request, provider).Read());
    }

    [Theory]
    [InlineData("not-a-protected-cookie")]
    [InlineData("")]
    public void InvalidCookieIsEmptyWithoutMutation(string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = ProtectedCookieCartStore.CookieName + "=" + value;
        Assert.Empty(Store(context, new EphemeralDataProtectionProvider()).Read());
        Assert.Empty(context.Response.Headers);
    }

    [Fact]
    public void OversizedCookieIsIgnored()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = ProtectedCookieCartStore.CookieName + "=" + new string('a', 5000);
        Assert.Empty(Store(context, new EphemeralDataProtectionProvider()).Read());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("not-json")]
    [InlineData("[{\"p\":1,\"v\":null,\"q\":0}]")]
    [InlineData("[{\"p\":-1,\"v\":null,\"q\":1}]")]
    [InlineData("[{\"p\":1,\"v\":-2,\"q\":1}]")]
    [InlineData("[{\"p\":1,\"v\":null,\"q\":1},{\"p\":1,\"v\":null,\"q\":2}]")]
    public void MalformedProtectedPayloadIsRejected(string payload)
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = provider.CreateProtector("Algara.Cart", "v1").ToTimeLimitedDataProtector();
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = ProtectedCookieCartStore.CookieName + "=" + protector.Protect(payload, TimeSpan.FromHours(1));
        Assert.Empty(Store(context, provider).Read());
    }

    [Fact]
    public void ExpiredCookieIsRejected()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = provider.CreateProtector("Algara.Cart", "v1").ToTimeLimitedDataProtector();
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = ProtectedCookieCartStore.CookieName + "=" +
            protector.Protect("[{\"p\":1,\"v\":null,\"q\":1}]", DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Empty(Store(context, provider).Read());
    }

    [Fact]
    public void CookieCannotBeReadWithAnotherDataProtectionKey()
    {
        var context = new DefaultHttpContext();
        Store(context, new EphemeralDataProtectionProvider()).Write([new(1, null, 1)]);
        var next = new DefaultHttpContext(); next.Request.Headers.Cookie = context.Response.Headers.SetCookie.ToString().Split(';')[0];
        Assert.Empty(Store(next, new EphemeralDataProtectionProvider()).Read());
    }

    [Fact]
    public void EmptyCartDeletesCookieWithinItsOriginalPath()
    {
        var context = new DefaultHttpContext(); context.Request.PathBase = "/shop";
        Store(context, new EphemeralDataProtectionProvider()).Write([]);
        var header = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("path=/shop", header);
        Assert.Contains("expires=Thu, 01 Jan 1970", header);
    }

    private static ProtectedCookieCartStore Store(HttpContext context, IDataProtectionProvider provider)
        => new(new HttpContextAccessor { HttpContext = context }, provider);
}

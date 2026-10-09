using System.Globalization;
using Algara.Web.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Algara.UnitTests.Models;

public class CatalogLocalizationTests
{
    [Theory]
    [InlineData(null, "bg")]
    [InlineData("c=bg-BG|uic=en", "en")]
    [InlineData("c=bg-BG|uic=bg", "bg")]
    [InlineData("c=en-US|uic=en", "en")]
    [InlineData("c=fr|uic=fr", "bg")]
    [InlineData("malformed", "bg")]
    public async Task CultureSelection_OnlyCookieChangesUiAndFormattingRemainsBulgarian(string? cookie, string expectedUi)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.AcceptLanguage = "en-US,en;q=0.9";
            context.Request.QueryString = new QueryString("?culture=en&ui-culture=en");
            if (cookie != null)
                context.Request.Headers.Cookie = CatalogLocalization.CookieName + "=" + Uri.EscapeDataString(cookie);
            var options = new RequestLocalizationOptions();
            CatalogLocalization.Configure(options);
            var middleware = new RequestLocalizationMiddleware(_ =>
            {
                Assert.Equal(expectedUi, CultureInfo.CurrentUICulture.Name);
                Assert.Equal("bg-BG", CultureInfo.CurrentCulture.Name);
                Assert.Equal(CultureInfo.GetCultureInfo("bg-BG").NumberFormat.NumberDecimalSeparator,
                    CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
                return Task.CompletedTask;
            }, Options.Create(options), NullLoggerFactory.Instance);

            await middleware.Invoke(context);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Theory]
    [InlineData("bg", "Каталог")]
    [InlineData("en", "Catalog")]
    [InlineData("fr", "Каталог")]
    public void CatalogResources_ResolveTranslationsAndNeutralBulgarianFallback(string language, string expected)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var services = new ServiceCollection().AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources").BuildServiceProvider();
            var localizer = services.GetRequiredService<IStringLocalizer<CatalogResources>>();

            Assert.Equal(expected, localizer["Catalog"].Value);
            Assert.False(localizer["Catalog"].ResourceNotFound);
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }
}

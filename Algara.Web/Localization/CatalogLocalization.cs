using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace Algara.Web.Localization;

public static class CatalogLocalization
{
    public const string CookieName = "Algara.Language";
    public const string FormattingCulture = "bg-BG";

    public static void Configure(RequestLocalizationOptions options)
    {
        // UI language never changes prices, currency, or the form-binding culture.
        var formattingCulture = CultureInfo.GetCultureInfo(FormattingCulture);
        var bulgarian = CultureInfo.GetCultureInfo("bg");
        // Use the same culture object before and after a cookie is chosen. Creating
        // the default from its name would apply this machine's user overrides only
        // on the first visit, causing decimal separators to change after selection.
        options.DefaultRequestCulture = new RequestCulture(formattingCulture, bulgarian);
        options.SupportedCultures = [formattingCulture];
        options.SupportedUICultures = [bulgarian, CultureInfo.GetCultureInfo("en")];
        options.FallBackToParentCultures = false;
        options.FallBackToParentUICultures = false;
        // English requires an explicit choice; ignore Accept-Language and query parameters.
        options.RequestCultureProviders = [new CookieRequestCultureProvider { CookieName = CookieName }];
    }
}

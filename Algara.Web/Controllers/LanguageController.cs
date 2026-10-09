using Algara.Web.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers;

public class LanguageController : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Select(string language, string? returnUrl = null)
    {
        if (language is not ("bg" or "en"))
            return BadRequest();

        Response.Cookies.Append(CatalogLocalization.CookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(CatalogLocalization.FormattingCulture, language)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = Request.PathBase.HasValue ? Request.PathBase.Value : "/"
            });

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/"));
    }
}

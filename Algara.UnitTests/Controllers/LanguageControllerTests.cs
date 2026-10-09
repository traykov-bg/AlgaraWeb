using Algara.Web.Controllers;
using Algara.Web.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace Algara.UnitTests.Controllers;

public class LanguageControllerTests
{
    [Theory]
    [InlineData("bg")]
    [InlineData("en")]
    public void Select_PersistsExplicitChoiceAndPreservesEntityAndFilters(string language)
    {
        var controller = CreateController();

        var result = Assert.IsType<LocalRedirectResult>(controller.Select(language, "/shop/kategorii/mebeli?sub=chairs&page=2"));

        Assert.Equal("/shop/kategorii/mebeli?sub=chairs&page=2", result.Url);
        var cookie = Uri.UnescapeDataString(controller.Response.Headers.SetCookie.ToString());
        Assert.Contains(CatalogLocalization.CookieName + "=" + CookieRequestCultureProvider.MakeCookieValue(
            new RequestCulture("bg-BG", language)), cookie);
        Assert.Contains("path=/shop", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en-US")]
    [InlineData("")]
    public void Select_UnsupportedChoiceDoesNotSetCookie(string language)
    {
        var controller = CreateController();

        Assert.IsType<BadRequestResult>(controller.Select(language, "/"));
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
    }

    [Theory]
    [InlineData("https://other.example/")]
    [InlineData("//other.example/")]
    [InlineData("/\\other.example/")]
    [InlineData(null)]
    public void Select_UnsafeOrMissingReturnUrlFallsBackToApplicationRoot(string? returnUrl)
    {
        var controller = CreateController();

        var result = Assert.IsType<LocalRedirectResult>(controller.Select("en", returnUrl));

        Assert.Equal("/shop/", result.Url);
    }

    [Fact]
    public void Select_RequiresPostAndAntiforgeryValidation()
    {
        var method = typeof(LanguageController).GetMethod(nameof(LanguageController.Select))!;
        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), false));
        Assert.Single(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false));
    }

    private static LanguageController CreateController()
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/shop";
        context.Request.Scheme = "https";
        var action = new ActionContext(context, new RouteData(), new ControllerActionDescriptor());
        return new LanguageController { ControllerContext = new ControllerContext(action), Url = new UrlHelper(action) };
    }
}

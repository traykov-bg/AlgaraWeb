using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Algara.UnitTests.Controllers;

public sealed class AdminProductFormValidationTests
{
    [Fact]
    public void GalleryFormWithoutPostedDisplayUrlsPassesMvcValidation()
    {
        var form = new AdminProductFormViewModel
        {
            N = 7,
            Name = "Product",
            Price = 123.45m,
            WidthCm = 123.45m,
            Images = [new() { N = 101, DisplayOrder = 0 }]
        };

        var state = Validate(form);

        Assert.True(state.IsValid, string.Join("; ", state.SelectMany(pair =>
            pair.Value!.Errors.Select(error => $"{pair.Key}: {error.ErrorMessage}"))));
    }

    [Fact]
    public void InvalidDimensionsAndGalleryOrderAreRejectedByMvcValidation()
    {
        var form = new AdminProductFormViewModel
        {
            Name = "Product",
            Price = 123.45m,
            WidthCm = 0m,
            HeightCm = 100000.01m,
            Images = [new() { N = 101, DisplayOrder = -1 }]
        };

        var state = Validate(form);

        Assert.False(state.IsValid);
        Assert.NotEmpty(state[nameof(form.WidthCm)]!.Errors);
        Assert.NotEmpty(state[nameof(form.HeightCm)]!.Errors);
        Assert.NotEmpty(state["Images[0].DisplayOrder"]!.Errors);
        Assert.False(state.ContainsKey("Images[0].ImageUrl"));
    }

    private static ModelStateDictionary Validate(AdminProductFormViewModel form)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        using var provider = services.BuildServiceProvider();
        var state = new ModelStateDictionary();
        var context = new ActionContext(new DefaultHttpContext { RequestServices = provider },
            new RouteData(), new ActionDescriptor(), state);
        provider.GetRequiredService<IObjectModelValidator>().Validate(context, null, string.Empty, form);
        return state;
    }
}

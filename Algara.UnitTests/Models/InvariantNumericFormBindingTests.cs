using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Algara.UnitTests.Models;

public class InvariantNumericFormBindingTests
{
    [Theory]
    [InlineData("bg")]
    [InlineData("en")]
    public async Task ShippingParcelsBindBrowserFractionalKilogramsAndCentimetres(string uiLanguage)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("bg-BG");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiLanguage);
            var fields = new Dictionary<string, StringValues>
            {
                ["ShippingParcels[0].WeightKg"] = "1.125",
                ["ShippingParcels[0].WidthCm"] = "11.5",
                ["ShippingParcels[0].HeightCm"] = "22.25",
                ["ShippingParcels[0].DepthCm"] = "33.75"
            };
            fields["__Invariant"] = new StringValues(fields.Keys.ToArray());
            var http = new DefaultHttpContext();
            http.Request.ContentType = "application/x-www-form-urlencoded";
            http.Request.Form = new FormCollection(fields);
            var action = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());
            var factory = new ValueProviderFactoryContext(action);
            await new FormValueProviderFactory().CreateValueProviderAsync(factory);
            var provider = new CompositeValueProvider(factory.ValueProviders);
            var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(decimal));
            var binder = new DecimalModelBinder(NumberStyles.Float | NumberStyles.AllowThousands, NullLoggerFactory.Instance);
            foreach (var (name, expected) in new[]
            {
                ("ShippingParcels[0].WeightKg", 1.125m), ("ShippingParcels[0].WidthCm", 11.5m),
                ("ShippingParcels[0].HeightCm", 22.25m), ("ShippingParcels[0].DepthCm", 33.75m)
            })
            {
                var binding = DefaultModelBindingContext.CreateBindingContext(action, provider, metadata, null, name);
                await binder.BindModelAsync(binding);
                Assert.Equal(0, binding.ModelState.ErrorCount);
                Assert.True(binding.Result.IsModelSet);
                Assert.Equal(expected, Assert.IsType<decimal>(binding.Result.Model));
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; CultureInfo.CurrentUICulture = originalUi; }
    }

    [Theory]
    [InlineData("bg")]
    [InlineData("en")]
    public async Task PromotionFields_BindBrowserDecimalValuesWithBulgarianFormatting(string uiLanguage)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("bg-BG");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiLanguage);
            var context = new DefaultHttpContext();
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["ProductRows[0].PromoPrice"] = "123.45",
                ["ProductRows[0].DiscountPercent"] = "12.375",
                ["LocalizedDecimal"] = "45,67",
                ["__Invariant"] = new StringValues(["ProductRows[0].PromoPrice", "ProductRows[0].DiscountPercent"])
            });
            var action = new ActionContext(context, new RouteData(), new ControllerActionDescriptor());
            var factoryContext = new ValueProviderFactoryContext(action);
            await new FormValueProviderFactory().CreateValueProviderAsync(factoryContext);
            var provider = new CompositeValueProvider(factoryContext.ValueProviders);
            var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(decimal));
            var binder = new DecimalModelBinder(NumberStyles.Float | NumberStyles.AllowThousands, NullLoggerFactory.Instance);

            foreach (var (field, expected) in new[]
            {
                ("ProductRows[0].PromoPrice", 123.45m),
                ("ProductRows[0].DiscountPercent", 12.375m),
                ("LocalizedDecimal", 45.67m)
            })
            {
                var binding = DefaultModelBindingContext.CreateBindingContext(action, provider, metadata, null, field);
                await binder.BindModelAsync(binding);

                Assert.Equal(0, binding.ModelState.ErrorCount);
                Assert.True(binding.Result.IsModelSet);
                Assert.Equal(expected, Assert.IsType<decimal>(binding.Result.Model));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }
}

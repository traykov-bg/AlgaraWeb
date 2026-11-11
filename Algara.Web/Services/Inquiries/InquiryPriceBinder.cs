using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace Algara.Web.Services.Inquiries;

// HTML number inputs submit a dot, while the shop's Bulgarian form culture uses a comma.
// Accept either decimal separator, but never grouping separators or exponent notation.
public sealed class InquiryPriceBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        var text = value.FirstValue?.Trim();
        if (string.IsNullOrEmpty(text)) { if (context.ModelType == typeof(decimal)) context.ModelState.TryAddModelError(context.ModelName, "Required price."); else context.Result = ModelBindingResult.Success(null); }
        else if (value.Values.Count == 1 && decimal.TryParse(text.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
            context.Result = ModelBindingResult.Success(amount);
        else context.ModelState.TryAddModelError(context.ModelName, "Invalid price.");
        return Task.CompletedTask;
    }
}

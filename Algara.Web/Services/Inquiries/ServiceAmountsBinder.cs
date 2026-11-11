using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace Algara.Web.Services.Inquiries;

public sealed class ServiceAmountsBinder : IModelBinder
{
    public static bool TryAmount(string? text, out decimal amount) => decimal.TryParse(text?.Trim().Replace(',', '.'),
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
    public Task BindModelAsync(ModelBindingContext context)
    {
        var amounts=new Dictionary<int,decimal>();
        if(context.HttpContext.Request.HasFormContentType)
        foreach(var pair in context.HttpContext.Request.Form.Where(p=>p.Key.StartsWith(context.ModelName+"[",StringComparison.Ordinal)))
        {
            context.ModelState.SetModelValue(pair.Key,new ValueProviderResult(pair.Value));
            var indexText=pair.Key[(context.ModelName.Length+1)..].TrimEnd(']');
            if(amounts.Count>=20 || !pair.Key.EndsWith(']') || !int.TryParse(indexText,out var index) || index<0 || index>=20 ||
                amounts.ContainsKey(index) || pair.Value.Count!=1 || !TryAmount(pair.Value[0],out var amount))
                context.ModelState.TryAddModelError(pair.Key,"Invalid service amount.");
            else amounts.Add(index,amount);
        }
        context.Result=ModelBindingResult.Success(amounts);
        return Task.CompletedTask;
    }
}

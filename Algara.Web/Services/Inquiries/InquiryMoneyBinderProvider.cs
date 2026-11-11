using Algara.Data.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace Algara.Web.Services.Inquiries;
public sealed class InquiryMoneyBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(decimal) &&
        (context.Metadata.ContainerType == typeof(AgreedOfferLine) || context.Metadata.ContainerType == typeof(AgreedService))
            ? new InquiryPriceBinder() : null;
}

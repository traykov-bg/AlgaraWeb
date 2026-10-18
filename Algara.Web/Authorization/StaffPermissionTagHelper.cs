using Algara.Identity.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Algara.Web.Authorization;

[HtmlTargetElement("*", Attributes = "staff-permission")]
public sealed class StaffPermissionTagHelper(StaffAccessService access) : TagHelper
{
    [HtmlAttributeName("staff-permission")]
    public string Permissions { get; set; } = string.Empty;

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("staff-permission");
        foreach (var key in Permissions.Split(','))
            if (!await access.HasPermissionAsync(ViewContext.HttpContext.User, key.Trim(), ViewContext.HttpContext.RequestAborted))
            {
                output.SuppressOutput();
                return;
            }
    }
}

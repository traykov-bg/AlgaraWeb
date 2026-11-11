using Algara.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Globalization;
namespace Algara.Web.Authorization;
[HtmlTargetElement("client-time", Attributes = "utc")]
public sealed class ClientTimeTagHelper(ClientTimeDisplay display) : TagHelper
{
    public DateTime Utc { get; set; }
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var value = await display.FormatAsync(Utc);
        output.TagName = "time"; output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.RemoveAll("utc");
        output.Attributes.SetAttribute("datetime", DateTime.SpecifyKind(Utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
        output.Attributes.SetAttribute("data-client-time", "");
        if (value.Zone != null) output.Attributes.SetAttribute("data-time-zone", value.Zone);
        output.Content.SetContent(value.Text);
    }
}

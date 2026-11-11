using System.ComponentModel.DataAnnotations;
using Algara.Data.Models;
namespace Algara.Web.ViewModels;
public sealed class InquiryForm
{
    public string SubmissionToken { get; set; } = "";
    public int? ProductN { get; set; }
    public int? VariantN { get; set; }
    [Range(1, 99)] public int Quantity { get; set; } = 1;
    public string? ProductSelection { get; set; }
    public bool Staff { get; set; }
    [Required, StringLength(121)] public string ContactName { get; set; } = "";
    [Required, StringLength(20), Phone] public string Phone { get; set; } = "";
    [StringLength(256)] public string? Email { get; set; }
    [StringLength(500)] public string? Dimensions { get; set; } = "";
    [StringLength(1000)] public string? Materials { get; set; } = "";
    [Required, StringLength(4000)] public string Description { get; set; } = "";
    [StringLength(2000)] public string? Reason { get; set; }
    public string Channel { get; set; } = "Phone";
    public bool Consent { get; set; }
    public List<IFormFile> Files { get; set; } = [];
    public string? Error { get; set; }
}
public sealed class InquiryUpdate
{
    [Range(0,1000000)] public int ScrollY { get; set; }
    public int N { get; set; }
    public string Version { get; set; } = "";
    public string Command { get; set; } = "";
    [StringLength(2000)] public string? Note { get; set; } = "";
    [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(Algara.Web.Services.Inquiries.InquiryPriceBinder))]
    public decimal? Price { get; set; }
    [StringLength(1000)] public string? Terms { get; set; } = "";
    [StringLength(4000)] public string? Description { get; set; } = "";
    public List<AgreedOfferLine> Lines { get; set; } = [];
    public List<AgreedService> Services { get; set; } = [];
    [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(Algara.Web.Services.Inquiries.InquiryPriceBinder))]
    public decimal? DeliveryAmount { get; set; }
    public string? ProductSearch { get; set; }
    public bool Agreement { get; set; }
}
public sealed class InquiryDetails
{
    public InquiryUpdate Draft { get; set; } = new();
    public FurnitureInquiry Inquiry { get; set; } = null!;
    public List<(int N, string FileName)> Attachments { get; set; } = [];
    public bool Staff { get; set; }
    public bool CanManage { get; set; }
    public bool CanConvert { get; set; }
    public List<AgreedOfferLine> Choices { get; set; } = [];
    public OrderStatus? LinkedOrderStatus { get; set; }
    public string? AccessLink { get; set; }
    public string? Error { get; set; }
}

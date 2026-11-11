namespace Algara.Data.Models;

public enum InquiryStatus { New, InProgress, Offered, Accepted, Declined, Closed, Converted }
public class FurnitureInquiry
{
    public int N { get; set; }
    public Guid SubmissionId { get; set; }
    public string GuestTokenHash { get; set; } = "";
    public string? CustomerId { get; set; }
    public string? CreatedByStaffId { get; set; }
    public string? AssignedStaffId { get; set; }
    public string Channel { get; set; } = "Web";
    public int? ProductN { get; set; }
    public int? VariantN { get; set; }
    public int? Quantity { get; set; }
    public int? OrderN { get; set; }
    public string? ProductName { get; set; }
    public string ContactName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string Dimensions { get; set; } = "";
    public string Materials { get; set; } = "";
    public string Description { get; set; } = "";
    public string Language { get; set; } = "bg";
    public InquiryStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<InquiryAttachment> Attachments { get; set; } = new List<InquiryAttachment>();
    public ICollection<InquiryEvent> Events { get; set; } = new List<InquiryEvent>();
    public ICollection<InquiryOffer> Offers { get; set; } = new List<InquiryOffer>();
}
public class InquiryAttachment
{
    public int N { get; set; }
    public int InquiryN { get; set; }
    public string FileName { get; set; } = "";
    public byte[] Contents { get; set; } = [];
}
public class InquiryEvent
{
    public int N { get; set; }
    public int InquiryN { get; set; }
    public string? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public DateTime AtUtc { get; set; }
    public InquiryStatus Status { get; set; }
    public string Action { get; set; } = "";
    public string Note { get; set; } = "";
}
public class InquiryOffer
{
    public int N { get; set; }
    public int InquiryN { get; set; }
    public decimal Price { get; set; }
    public string? LinesJson { get; set; }
    public string? ServicesJson { get; set; }
    public decimal? DeliveryAmount { get; set; }
    public string Terms { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime AtUtc { get; set; }
    public string ActorId { get; set; } = "";
}

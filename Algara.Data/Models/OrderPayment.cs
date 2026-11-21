namespace Algara.Data.Models;
public enum PaymentEntryKind { Receipt = 0, Correction = 1, Refund = 2, Allocation = 3 }
public class PaymentMethod
{
 public int N { get; set; }
 public string Name { get; set; } = "";
 public string? EnglishName { get; set; }
 public bool IsActive { get; set; } = true;
 public byte[] RowVersion { get; set; } = [];
}
public class PaymentPolicy
{
 public int N { get; set; } = 1;
 public decimal DepositPercent { get; set; } = 50m;
 public byte[] RowVersion { get; set; } = [];
}
public class OrderPayment
{
 public long N { get; set; }
 public int OrderN { get; set; }
 public Order Order { get; set; } = null!;
 public Guid SubmissionId { get; set; }
 public PaymentEntryKind Kind { get; set; }
 public decimal Amount { get; set; }
 public int? MethodN { get; set; }
 public string MethodName { get; set; } = "";
 public string Location { get; set; } = "";
 public DateOnly PaidOn { get; set; }
 public DateTime RecordedAtUtc { get; set; }
 public string ActorId { get; set; } = "";
 public string ActorName { get; set; } = "";
 public string? Note { get; set; }
 public long? ReversesN { get; set; }
 public int? RelatedOrderN { get; set; }
}

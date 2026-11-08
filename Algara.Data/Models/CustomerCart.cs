namespace Algara.Data.Models;
public sealed class CustomerCart
{
 public string UserId { get; set; } = "";
 public string Contents { get; set; } = "[]";
 public DateTime UpdatedAtUtc { get; set; }
}
public sealed class GuestCartClaim
{
 public Guid Id { get; set; }
 public string UserId { get; set; } = "";
 public DateTime ClaimedAtUtc { get; set; }
}

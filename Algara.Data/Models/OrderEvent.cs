namespace Algara.Data.Models;

public sealed class OrderEvent
{
    public long N { get; set; }
    public int OrderN { get; set; }
    public Order Order { get; set; } = null!;
    public string Action { get; set; } = "";
    public string? Details { get; set; }
    public string ActorId { get; set; } = "";
    public OrderStatus Status { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

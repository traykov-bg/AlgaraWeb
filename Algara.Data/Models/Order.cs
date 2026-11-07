namespace Algara.Data.Models
{
    public enum OrderStatus
    {
        Pending = 0,
        Confirmed = 1,
        Shipped = 2,
        Delivered = 3,
        Cancelled = 4
    }

    public class Order
    {
        public int N { get; set; }

        /// <summary>
        /// FK към Users.N — без навигационно свойство, за да се избегне циклична референция
        /// между Algara.Data и Algara.Identity. FK constraint-ът се добавя ръчно в миграцията.
        /// </summary>
        public int? UserN { get; set; }

        public Guid? SubmissionId { get; set; }
        public string? GuestTokenHash { get; set; }
        public string? ContactName { get; set; }
        public string? ContactEmail { get; set; }
        public string? ContactPhone { get; set; }
        public string? DeliverySnapshot { get; set; }
        public string? WaybillNumber { get; set; }
        public string? Comment { get; set; }
        public string? InvoiceDetails { get; set; }
        public decimal DeliveryAmount { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public DateTime? ConfirmedAtUtc { get; set; }
        public DateTime? TermsAcceptedAtUtc { get; set; }
        public byte[] RowVersion { get; set; } = [];

        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}

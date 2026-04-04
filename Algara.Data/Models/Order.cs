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
        public int UserN { get; set; }

        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}

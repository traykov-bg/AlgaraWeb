namespace Algara.Data.Models
{
    public class OrderItem
    {
        public int N { get; set; }
        public int OrderN { get; set; }
        public int ProductN { get; set; }
        public int Quantity { get; set; }

        /// <summary>
        /// Снимка на цената в момента на поръчката — не се обновява при промяна на Product.Price.
        /// </summary>
        public decimal UnitPrice { get; set; }

        public Order Order { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}

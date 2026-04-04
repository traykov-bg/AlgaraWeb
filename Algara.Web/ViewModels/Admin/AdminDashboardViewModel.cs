namespace Algara.Web.ViewModels.Admin
{
    public class AdminDashboardViewModel
    {
        public int TotalActiveProducts { get; set; }
        public int TotalCategories     { get; set; }
        public int TotalUsers          { get; set; }

        public int OrdersPending   { get; set; }
        public int OrdersConfirmed { get; set; }
        public int OrdersShipped   { get; set; }
        public int OrdersDelivered { get; set; }
        public int OrdersCancelled { get; set; }

        public int TotalOrders =>
            OrdersPending + OrdersConfirmed + OrdersShipped + OrdersDelivered + OrdersCancelled;
    }
}

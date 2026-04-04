using Algara.Data.Models;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminOrderRowViewModel
    {
        public Order  Order           { get; set; } = null!;
        public string UserDisplayName { get; set; } = string.Empty;
        public string UserEmail       { get; set; } = string.Empty;
    }

    public class AdminOrderListViewModel
    {
        public IEnumerable<AdminOrderRowViewModel> Rows         { get; set; } = [];
        public OrderStatus?                        StatusFilter { get; set; }
    }
}

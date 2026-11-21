using Algara.Data.Models;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminOrderDetailViewModel
    {
        public Dictionary<OrderStatus, Algara.Web.Services.Payments.OrderStatusPaymentNotice> PaymentNotices { get; set; } = [];
        public bool CanOverridePayment { get; set; }
        public IReadOnlyDictionary<string, string> ActorNames { get; set; } = new Dictionary<string, string>();
        public IReadOnlyList<OrderEvent> History { get; set; } = [];
        public Order                   Order           { get; set; } = null!;
        public string                  UserDisplayName { get; set; } = string.Empty;
        public string                  UserEmail       { get; set; } = string.Empty;
        public IEnumerable<OrderStatus> AllStatuses    { get; set; } = Enum.GetValues<OrderStatus>();
    }
}

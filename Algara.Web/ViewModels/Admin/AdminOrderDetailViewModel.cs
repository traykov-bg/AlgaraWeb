using Algara.Data.Models;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminOrderDetailViewModel
    {
        public IReadOnlyDictionary<string, string> ActorNames { get; set; } = new Dictionary<string, string>();
        public IReadOnlyList<OrderEvent> History { get; set; } = [];
        public Order                   Order           { get; set; } = null!;
        public string                  UserDisplayName { get; set; } = string.Empty;
        public string                  UserEmail       { get; set; } = string.Empty;
        public IEnumerable<OrderStatus> AllStatuses    { get; set; } = Enum.GetValues<OrderStatus>();
    }
}

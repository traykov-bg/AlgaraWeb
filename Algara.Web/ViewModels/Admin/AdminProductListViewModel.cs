using Algara.Data.Models;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminProductListViewModel
    {
        public IEnumerable<Product> Products    { get; set; } = [];
        public int                  CurrentPage { get; set; }
        public int                  TotalPages  { get; set; }
        public int                  PageSize    { get; set; } = 20;
    }
}

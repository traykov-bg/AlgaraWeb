using Algara.Identity.Models;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminUserRowViewModel
    {
        public ApplicationUser User  { get; set; } = null!;
        public List<string>    Roles { get; set; } = [];
    }

    public class AdminUserListViewModel
    {
        public IEnumerable<AdminUserRowViewModel> Rows { get; set; } = [];
    }
}

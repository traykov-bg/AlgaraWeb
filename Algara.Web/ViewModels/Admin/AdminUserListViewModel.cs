using Algara.Identity.Models;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminUserSummaryViewModel
    {
        public int N { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public bool IsActive { get; set; }
        // Used exclusively by the existing Admin-only role-management forms.
        public string? SecurityStamp { get; set; }
    }

    public class AdminUserRowViewModel
    {
        public AdminUserSummaryViewModel User { get; set; } = null!;
        public List<string>    Roles { get; set; } = [];
    }

    public class AdminUserListViewModel
    {
        public IEnumerable<AdminUserRowViewModel> Rows { get; set; } = [];
        public string? Search { get; set; }
        public bool? Active { get; set; }
        public int Page { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int Total { get; set; }
    }
}

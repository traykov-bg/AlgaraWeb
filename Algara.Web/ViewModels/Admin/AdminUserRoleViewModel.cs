namespace Algara.Web.ViewModels.Admin
{
    public class AdminUserRoleViewModel
    {
        public string Username { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
        [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
        public string? ExpectedSecurityStamp { get; set; }
    }
}

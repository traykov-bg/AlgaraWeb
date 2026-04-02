using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

public interface IUserService
{
    // Автентикация
    Task<ApplicationUser?> GetUserByUsernameAsync(string username);
    Task<bool> RegisterUserAsync(string username, string email, string password);
    Task<bool> ValidateUserAsync(string username, string password);
    Task ValidateSecurityStampAsync(CookieValidatePrincipalContext context);
    Task SignInAsync(HttpContext httpContext, ApplicationUser user, bool rememberMe, int? timeZoneOffset = null);
    Task SignOutAsync(HttpContext httpContext);
    Task<UserSession?> GetActiveSessionAsync(int userN, string sessionId);
    Task ForceSignOutAllSessionsAsync(string userId);

    // Пароли
    Task<bool> ChangePasswordAsync(string username, string newPassword);

    // Роли
    Task<IList<string>> GetRolesAsync(ApplicationUser user);
    Task<bool> IsInRoleAsync(ApplicationUser user, string roleName);
    Task<bool> AddUserToRoleAsync(string username, string roleName);
    Task<bool> RemoveUserFromRoleAsync(string username, string roleName);

    // Claims
    Task<List<Claim>> GetUserClaimsAsync(ApplicationUser user);
}

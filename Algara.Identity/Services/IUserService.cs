using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Query;

public interface IUserService
{
    Task<ApplicationUser?> GetUserByUsernameAsync(string username);
    Task<bool> RegisterUserAsync(string username, string email, string password);
    Task<bool> ValidateUserAsync(string username, string password);
    Task ValidateSecurityStampAsync(CookieValidatePrincipalContext context);
    Task SignInAsync(HttpContext httpContext, ApplicationUser user, bool rememberMe);
    Task<UserSession?> GetActiveSessionAsync(int userN, string sessionId);
    Task ForceSignOutAllSessionsAsync(string userN);
}
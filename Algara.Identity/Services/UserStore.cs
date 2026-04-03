using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Algara.Identity.Models;
using Algara.Utils;

namespace Algara.Identity.Services
{
    public class UserStore : IUserStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>, IUserRoleStore<ApplicationUser>
    {
        private readonly IDatabaseHelper _databaseHelper;

        public UserStore(IDatabaseHelper databaseHelper)
        {
            _databaseHelper = databaseHelper;
        }

        public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = "INSERT INTO Users (N, Id, UserName, DisplayName, Email, PasswordHash) VALUES (@UserN, @Id, @UserName, @DisplayName, @Email, @PasswordHash)";
            var parameters = new
            {
                user.N,
                user.Id,
                user.UserName,
                user.DisplayName,
                user.Email,
                user.PasswordHash
            };

            int result = await _databaseHelper.ExecuteAsync(query, parameters);
            return result > 0 ? IdentityResult.Success : IdentityResult.Failed();
        }

        public async Task<ApplicationUser> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = "SELECT N, Id, UserName, DisplayName, Email, PasswordHash, SecurityStamp, LastLoginSessionId FROM Users WHERE Id = @Id";
            return await _databaseHelper.QuerySingleAsync<ApplicationUser>(query, new { Id = userId });
        }

        public async Task<ApplicationUser> FindByNAsync(int userN, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = "SELECT N, Id, UserName, DisplayName, Email, PasswordHash, SecurityStamp, LastLoginSessionId FROM Users WHERE N = @N";
            return await _databaseHelper.QuerySingleAsync<ApplicationUser>(query, new { N = userN });
        }

        public async Task<ApplicationUser> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = "SELECT N, Id, UserName, DisplayName, Email, PasswordHash, SecurityStamp, LastLoginSessionId FROM Users WHERE LOWER(UserName) = @UserName";
            return await _databaseHelper.QuerySingleAsync<ApplicationUser>(query, new { UserName = normalizedUserName.ToLower() });
        }

        public async Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return await Task.FromResult(user.Id);
        }

        public async Task<int> GetUserNAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return await Task.FromResult(user.N);
        }

        public async Task<string> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return await Task.FromResult(user.UserName);
        }

        public async Task SetUserNameAsync(ApplicationUser user, string userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            await Task.CompletedTask;
        }

        public async Task<string> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return await Task.FromResult(user.UserName.ToLower());
        }

        public async Task SetNormalizedUserNameAsync(ApplicationUser user, string normalizedName, CancellationToken cancellationToken)
        {
            user.UserName = normalizedName.ToLower();
            await Task.CompletedTask;
        }

        public async Task SetPasswordHashAsync(ApplicationUser user, string passwordHash, CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            await Task.CompletedTask;
        }

        public async Task<string> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return await Task.FromResult(user.PasswordHash);
        }

        public async Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return await Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
        }

        public void Dispose() { }

        public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = "UPDATE Users SET UserName = @UserName, DisplayName = @DisplayName, Email = @Email, PasswordHash = @PasswordHash WHERE N = @N";
            var parameters = new
            {
                user.UserName,
                user.DisplayName,
                user.Email,
                user.PasswordHash,
                user.N
            };

            int result = await _databaseHelper.ExecuteAsync(query, parameters);
            return result > 0 ? IdentityResult.Success : IdentityResult.Failed();
        }

        public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = "DELETE FROM Users WHERE N = @N";
            var parameters = new { user.N };

            int result = await _databaseHelper.ExecuteAsync(query, parameters);
            return result > 0 ? IdentityResult.Success : IdentityResult.Failed();
        }

        public async Task AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = @"
        INSERT INTO UserRoles (UserN, RoleN) 
        SELECT @UserId, N FROM Roles WHERE Name = @RoleName";

            var parameters = new { UserId = user.N, RoleName = roleName };
            await _databaseHelper.ExecuteAsync(query, parameters);
        }

        public async Task RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = @"
        DELETE FROM UserRoles 
        WHERE UserN = @UserN
        AND RoleN = (SELECT N FROM Roles WHERE Name = @RoleName)";

            var parameters = new { UserN = user.N, RoleName = roleName };
            await _databaseHelper.ExecuteAsync(query, parameters);
        }

        public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = @"
        SELECT r.Name 
        FROM Roles r
        INNER JOIN UserRoles ur ON r.N = ur.RoleN
        WHERE ur.UserN = @UserN";

            var roles = await _databaseHelper.QueryAsync<string>(query, new { UserN = user.N });
            return roles.ToList();
        }

        public async Task<bool> IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = @"
        SELECT COUNT(1) 
        FROM UserRoles ur
        INNER JOIN Roles r ON ur.RoleN = r.N
        WHERE ur.UserN = @UserN AND r.Name = @RoleName";

            int count = await _databaseHelper.QuerySingleAsync<int>(query, new { UserN = user.N, RoleName = roleName });
            return count > 0;
        }

        public async Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string query = @"
        SELECT u.Id, u.N, u.UserName, u.DisplayName, u.Email, u.PasswordHash, u.SecurityStamp, u.LastLoginSessionId
        FROM Users u
        INNER JOIN UserRoles ur ON u.N = ur.UserN
        INNER JOIN Roles r ON ur.RoleN = r.N
        WHERE r.Name = @RoleName";

            var users = await _databaseHelper.QueryAsync<ApplicationUser>(query, new { RoleName = roleName });
            return users.ToList();
        }
    }
}
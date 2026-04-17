using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using System.Net.Http;
using static Dapper.SqlMapper;
using Microsoft.Extensions.Logging;
using System;

namespace Algara.Identity.Services
{
    public class UserService : IUserService
    {
        private readonly IdentityDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<UserService> _logger;

        public UserService(IdentityDbContext context, IHttpContextAccessor httpContextAccessor, ILogger<UserService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<ApplicationUser?> GetUserByUsernameAsync(string username)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
        }

        public async Task<bool> RegisterUserAsync(string username, string email, string password)
        {
            if (await _context.Users.AnyAsync(u => u.UserName == username || u.Email == email))
                return false; // Вече съществува такъв потребител

            string salt = GenerateSalt();
            string passwordHash = HashPassword(password, salt);

            var user = new ApplicationUser
            {
                UserName = username,
                DisplayName = username,
                Email = email,
                PasswordHash = passwordHash,
                Salt = salt,
                FullName = username
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<ApplicationUser?> RegisterUserAsync(RegistrationData data)
        {
            if (await _context.Users.AnyAsync(u => u.UserName == data.Email || u.Email == data.Email))
                return null;

            string salt = GenerateSalt();
            string passwordHash = HashPassword(data.Password, salt);

            var fullName = $"{data.FirstName} {data.LastName}".Trim();
            var user = new ApplicationUser
            {
                UserName = data.Email,
                DisplayName = fullName,
                Email = data.Email,
                PasswordHash = passwordHash,
                Salt = salt,
                FullName = fullName,
                FirstName = data.FirstName,
                LastName = data.LastName,
                PhoneNumber = string.IsNullOrWhiteSpace(data.PhoneNumber) ? null : data.PhoneNumber.Trim(),
                MarketingConsent = data.MarketingConsent
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(); // записваме, за да получим user.N

            var consents = new List<UserConsent>
            {
                BuildConsent(user.N, ConsentTypes.Terms,    granted: true,                     data),
                BuildConsent(user.N, ConsentTypes.Privacy,  granted: true,                     data),
                BuildConsent(user.N, ConsentTypes.Age18,    granted: true,                     data),
                BuildConsent(user.N, ConsentTypes.Marketing, granted: data.MarketingConsent,    data)
            };
            _context.UserConsents.AddRange(consents);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Регистриран нов потребител {Email} с consent audit trail ({Count} записа)", data.Email, consents.Count);
            return user;
        }

        private static UserConsent BuildConsent(int userN, string type, bool granted, RegistrationData data) => new()
        {
            UserN = userN,
            ConsentType = type,
            Granted = granted,
            PolicyVersion = data.PolicyVersion,
            IpAddress = data.IpAddress,
            UserAgent = data.UserAgent,
            ConsentedAt = DateTime.UtcNow
        };

        public async Task<bool> ValidateUserAsync(string username, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
            if (user == null)
            {
                _logger.LogWarning($"Неуспешен опит за вход с несъществуващо потребителско име: {username}");
                return false;
            }

            // Проверяваме дали акаунтът е заключен
            if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.Now)
            {
                _logger.LogWarning($"Заключен акаунт опит за вход: {username}");
                return false;
            }

            string hashedPassword = HashPassword(password, user.Salt);
            if (hashedPassword != user.PasswordHash)
            {
                user.FailedLoginAttempts++;

                if (user.FailedLoginAttempts >= 5) // Например, заключваме след 5 грешни опита
                {
                    user.LockoutUntil = DateTime.Now.AddMinutes(15); // Заключване за 15 минути
                    _logger.LogWarning($"Потребителят {username} беше заключен за 15 минути след {user.FailedLoginAttempts} неуспешни опита.");
                }
                else
                {
                    _logger.LogWarning($"Неуспешен опит за вход за {username}. Брой грешни опити: {user.FailedLoginAttempts}");
                }

                await _context.SaveChangesAsync();
                return false;
            }

            // Успешен вход – нулираме брояча и заключването
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Успешен вход за {username}");
            return true;
        }

        private string GenerateSalt()
        {
            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            return Convert.ToBase64String(salt);
        }

        private string HashPassword(string password, string salt)
        {
            return Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: Convert.FromBase64String(salt),
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 10000,
                numBytesRequested: 32
            ));
        }

        public async Task<bool> ChangePasswordAsync(string username, string newPassword)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
            if (user == null)
                return false;

            string salt = GenerateSalt();
            string passwordHash = HashPassword(newPassword, salt);

            user.PasswordHash = passwordHash;
            user.Salt = salt;
            user.SecurityStamp = Guid.NewGuid().ToString(); // Генерираме нов SecurityStamp

            _context.Users.Update(user);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task ValidateSecurityStampAsync(CookieValidatePrincipalContext context)
        {
            var userId = context.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var securityStamp = context.Principal.FindFirst("SecurityStamp")?.Value;
            var sessionId = context.Principal.FindFirst("SessionId")?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(securityStamp) || string.IsNullOrEmpty(sessionId))
            {
                await context.HttpContext.SignOutAsync();
                context.RejectPrincipal();
                return;
            }

            var userStore = context.HttpContext.RequestServices.GetRequiredService<IUserStore<ApplicationUser>>();
            var user = await userStore.FindByIdAsync(userId, CancellationToken.None); 
            var session = await _context.UserSessions.FirstOrDefaultAsync(us => us.UserN == user.N && us.SessionId == sessionId && us.IsActive);

            if (user == null || user.SecurityStamp != securityStamp || session == null)
            {
                await context.HttpContext.SignOutAsync();
                context.RejectPrincipal();
            }
        }

        public async Task SignInAsync(HttpContext httpContext, ApplicationUser user, bool rememberMe, int? timeZoneOffset = null)
        {
            var activeSessions = await _context.UserSessions
                .Where(us => us.UserN == user.N && us.IsActive)
                .OrderBy(us => us.CreatedAt)
                .ToListAsync();

            // Ако има повече от 10 активни сесии, изтриваме най-старата
            if (activeSessions.Count >= 10)
            {
                var oldestSession = activeSessions.First();
                oldestSession.IsActive = false;
                _context.UserSessions.Update(oldestSession);
            }

            var session = new UserSession
            {
                UserN = user.N,
                SessionId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.Now,
                DeviceInfo = "",
                IsActive = true,
                TimeZoneOffset = timeZoneOffset
            };
            _context.UserSessions.Add(session);

            user.LastLoginSessionId = session.SessionId;
            user.LastLoginDate = DateTime.Now;
            await _context.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id), // UserId
                new Claim(ClaimTypes.Name, user.UserName), // Username
                new Claim(ClaimTypes.Email, user.Email), // Email
                new Claim("SecurityStamp", user.SecurityStamp), // SecurityStamp за валидация
                new Claim("SessionId", session.SessionId) // Добавяме SessionId
            };

            var roles = await GetRolesAsync(user);
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe, // Ако е true, запазваме сесията
                ExpiresUtc = rememberMe ? DateTime.UtcNow.AddDays(14) : DateTime.UtcNow.AddMinutes(30)
            };

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
        }

        public async Task SignOutAsync(HttpContext httpContext)
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var sessionId = httpContext.User.FindFirst("SessionId")?.Value;

            if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(sessionId))
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user != null)
                {
                    var session = await _context.UserSessions
                        .FirstOrDefaultAsync(us => us.UserN == user.N && us.SessionId == sessionId);

                    if (session != null)
                    {
                        session.IsActive = false;
                        await _context.SaveChangesAsync();
                    }
                }
            }

            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        public async Task<IList<string>> GetRolesAsync(ApplicationUser user)
        {
            return await _context.UserRoles
                .Where(ur => ur.UserN == user.N) // Сравняваме по N, а не Id
                .Select(ur => ur.Role.Name) // Извличаме името на ролята
                .ToListAsync();
        }

        public async Task<bool> IsInRoleAsync(ApplicationUser user, string roleName)
        {
            return await _context.UserRoles
                .AnyAsync(ur => ur.UserN == user.N && ur.Role.Name == roleName);
        }

        public async Task<bool> AddUserToRoleAsync(string username, string roleName)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
            if (user == null) return false;

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role == null) return false;

            // Проверяваме дали вече има тази роля
            bool hasRole = await _context.UserRoles.AnyAsync(ur => ur.UserN == user.N && ur.RoleN == role.N);
            if (hasRole) return false;

            // Добавяме роля
            var userRole = new UserRole { UserN = user.N, RoleN = role.N };
            _context.UserRoles.Add(userRole);
            await _context.SaveChangesAsync();

            await UpdateUserClaimsAsync(user); // 🔄 Обновяване на claims

            return true;
        }

        public async Task<bool> RemoveUserFromRoleAsync(string username, string roleName)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
            if (user == null) return false;

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role == null) return false;

            var userRole = await _context.UserRoles.FirstOrDefaultAsync(ur => ur.UserN == user.N && ur.RoleN == role.N);
            if (userRole == null) return false;

            _context.UserRoles.Remove(userRole);
            await _context.SaveChangesAsync();

            await UpdateUserClaimsAsync(user); // 🔄 Обновяване на claims

            return true;
        }

        private async Task UpdateUserClaimsAsync(ApplicationUser user)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return;

            // Обновяваме сесията само ако потребителят е текущо влезлият —
            // при промяна на роли от администратор не трябва да подменяме неговата сесия.
            var currentUserId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId != user.Id) return;

            var claims = await GetUserClaimsAsync(user);
            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties { IsPersistent = true };
            var principal = new ClaimsPrincipal(claimsIdentity);

            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
        }

        public async Task<List<Claim>> GetUserClaimsAsync(ApplicationUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim("SecurityStamp", user.SecurityStamp ?? ""),
                new Claim("SessionId", user.LastLoginSessionId??"") // 
            };

            var roles = await GetRolesAsync(user);
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            return claims;
        }

        public async Task<UserSession?> GetActiveSessionAsync(int userN, string sessionId)
        {
            return await _context.UserSessions
                .FirstOrDefaultAsync(us => us.UserN == userN && us.SessionId == sessionId && us.IsActive);
        }

        public async Task ForceSignOutAllSessionsAsync(string userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                _logger.LogWarning($"Опит за принудително излизане на несъществуващ потребител с ID: {userId}");
                return;
            }

            // Деактивираме всички сесии
            var sessions = await _context.UserSessions.Where(us => us.UserN == user.N).ToListAsync();
            foreach (var session in sessions)
            {
                session.IsActive = false;
            }

            // Обновяваме SecurityStamp (принуждава всички да се излогнат)
            user.SecurityStamp = Guid.NewGuid().ToString();

            await _context.SaveChangesAsync();
        }
    }
}

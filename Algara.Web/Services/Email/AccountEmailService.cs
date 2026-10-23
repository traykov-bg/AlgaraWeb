using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services.Email;

public sealed class AccountEmailService(ShopDbContext shop, EmailOutboxFactory outbox,
    EmailDeliveryReadiness readiness, TimeProvider clock) : IAccountEmailService
{
    private const string Unavailable = "Изпращането на имейли временно не е достъпно. Опитайте по-късно.";
    private const string StaffEmailUnavailable = "Изпращането на имейли не е настроено. Администратор трябва да провери публичния адрес на сайта, включването на SMTP и запазената парола в „Имейл настройки“.";
    private const string RateLimited = "Вече е поискана връзка. Изчакайте поне минута; разрешени са до 5 заявки за час.";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AccountEmailResult> RegisterAsync(RegistrationData data, CancellationToken cancellationToken = default)
    {
        try
        {
            var email = AccountEmailRules.NormalizeEmail(data.Email);
            if (email == null || !ValidRegistration(data)) return new(false, "Проверете данните, паролата и приемането на условията.");
            var site = await readiness.GetPublicSiteUrlAsync(cancellationToken);
            if (site == null) return new(false, Unavailable);
            return await ExecuteAsync(async identity =>
            {
                if (await identity.Users.AnyAsync(u => u.Email == email || u.UserName == email, cancellationToken))
                    return new(false, "Имейлът вече е регистриран. Използвайте вход или повторно потвърждение.");
                var user = await CreateUserAsync(identity, email, data, confirmed: false, cancellationToken);
                await IssueForUserAsync(identity, user, AccountEmailPurpose.EmailConfirmation, site, null, "Нова регистрация", cancellationToken);
                return new(true);
            }, false, cancellationToken);
        }
        finally { data.Password = string.Empty; }
    }

    public Task RequestResetAsync(string email, CancellationToken cancellationToken = default) =>
        RequestPublicAsync(email, AccountEmailPurpose.PasswordReset, cancellationToken);

    public Task RequestConfirmationAsync(string email, CancellationToken cancellationToken = default) =>
        RequestPublicAsync(email, AccountEmailPurpose.EmailConfirmation, cancellationToken);

    private async Task RequestPublicAsync(string submittedEmail, AccountEmailPurpose purpose, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var minimum = TimeSpan.FromMilliseconds(450 + RandomNumberGenerator.GetInt32(100));
        try
        {
        var email = AccountEmailRules.NormalizeEmail(submittedEmail);
        if (email == null) return;
        var site = await readiness.GetPublicSiteUrlAsync(cancellationToken);
        if (site == null) return;
        // The caller always returns the same public response, including unavailable/throttled cases.
        await ExecuteAsync(async identity =>
        {
            var user = await identity.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK,HOLDLOCK) WHERE [Email]={email}")
                .SingleOrDefaultAsync(cancellationToken);
            if (user == null || !user.IsActive || (purpose == AccountEmailPurpose.EmailConfirmation && user.EmailConfirmed)) return new(true);
            if (!await MayIssueAsync(identity, email, purpose, cancellationToken)) return new(true);
            await IssueForUserAsync(identity, user, purpose, site, null, "Заявка от публичната форма", cancellationToken);
            return new(true);
        }, false, cancellationToken);
        }
        finally
        {
            // Queue work rather than SMTP in this request and pad ordinary short paths
            // equally, including unknown addresses, disabled SMTP and throttled requests.
            var remaining = minimum - started.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
        }
    }

    public Task<AccountEmailResult> SendResetAsync(ClaimsPrincipal actor, int userN, string? expectedAccountVersion,
        string? reason, CancellationToken cancellationToken = default) =>
        SendByStaffAsync(actor, userN, expectedAccountVersion, reason, AccountEmailPurpose.PasswordReset, cancellationToken);

    public Task<AccountEmailResult> SendConfirmationAsync(ClaimsPrincipal actor, int userN, string? expectedAccountVersion,
        string? reason, CancellationToken cancellationToken = default) =>
        SendByStaffAsync(actor, userN, expectedAccountVersion, reason, AccountEmailPurpose.EmailConfirmation, cancellationToken);

    private async Task<AccountEmailResult> SendByStaffAsync(ClaimsPrincipal actor, int userN, string? version,
        string? reason, AccountEmailPurpose purpose, CancellationToken cancellationToken)
    {
        reason = reason?.Trim();
        if (userN <= 0 || !CustomerDossierVersions.IsAccountVersion(version) || !ValidReason(reason))
            return new(false, "Презаредете досието и въведете основание до 2000 знака.");
        var site = await readiness.GetPublicSiteUrlAsync(cancellationToken);
        if (site == null) return new(false, StaffEmailUnavailable);
        return await ExecuteAsync(async identity =>
        {
            var staff = await FindStaffAsync(identity, actor, invitations: false, cancellationToken);
            if (staff == null) return new(false, "Нямате актуално право за изпращане на имейли за достъп.");
            var user = await LoadUserAsync(identity, userN, cancellationToken);
            if (user == null || CustomerDossierVersions.Account(user) != version || !user.IsActive)
                return new(false, "Акаунтът е променен или не е активен. Презаредете досието.");
            if (!await MayManageTargetAsync(identity, staff, user, cancellationToken))
                return new(false, "Само Администратор може да задейства възстановяване за служебен акаунт.");
            if (purpose == AccountEmailPurpose.EmailConfirmation && user.EmailConfirmed)
                return new(false, "Имейлът вече е потвърден.");
            if (!await MayIssueAsync(identity, user.Email, purpose, cancellationToken)) return new(false, RateLimited);
            if (AccountEmailRules.NormalizeEmail(user.Email) == null) return new(false, "Акаунтът няма валиден имейл адрес.");
            await IssueForUserAsync(identity, user, purpose, site, staff, reason!, cancellationToken);
            return new(true);
        }, false, cancellationToken);
    }

    public Task<AccountEmailResult> ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default)
    {
        if (!AccountEmailRules.ValidPassword(password)) return Task.FromResult(new AccountEmailResult(false, "Паролата трябва да е между 6 и 1024 знака."));
        return ConsumeForUserAsync(token, AccountEmailPurpose.PasswordReset, password, cancellationToken);
    }

    public Task<AccountEmailResult> ConfirmEmailAsync(string token, CancellationToken cancellationToken = default) =>
        ConsumeForUserAsync(token, AccountEmailPurpose.EmailConfirmation, null, cancellationToken);

    private async Task<AccountEmailResult> ConsumeForUserAsync(string value, AccountEmailPurpose purpose,
        string? password, CancellationToken cancellationToken)
    {
        if (!AccountEmailRules.TryParse(value, out var id, out var secret)) return new(false, AccountEmailRules.InvalidLink);
        return await ExecuteAsync(async identity =>
        {
            var token = await LoadTokenAsync(identity, id, cancellationToken);
            if (!ValidToken(token, purpose, secret) || token!.UserN == null) return new(false, AccountEmailRules.InvalidLink);
            var user = await LoadUserAsync(identity, token.UserN.Value, cancellationToken);
            if (!MatchesUser(token, user)) return new(false, AccountEmailRules.InvalidLink);
            var now = Now;
            token.ConsumedAtUtc = now;
            if (purpose == AccountEmailPurpose.PasswordReset)
            {
                var credential = AccountPasswordCodec.Create(password!);
                user!.PasswordHash = credential.Hash; user.Salt = credential.Salt;
                user.SecurityStamp = Guid.NewGuid().ToString();
                user.FailedLoginAttempts = 0; user.LockoutUntil = null;
                foreach (var session in await identity.UserSessions.Where(s => s.UserN == user.N && s.IsActive).ToListAsync(cancellationToken))
                    session.IsActive = false;
                await RevokeAsync(identity, identity.AccountEmailTokens.Where(t => t.UserN == user.N), cancellationToken);
                // Notification has no token and cannot grant access. Queue atomically with the password change.
                identity.EmailOutboxMessages.Add(outbox.Create(user.Email, "Паролата ви в Алгара е променена",
                    "Паролата на вашия акаунт беше променена. Ако не сте поискали промяната, свържете се с магазина.", null,
                    now.AddDays(1), null));
                Audit(identity, token, user, "PasswordReset", "Паролата е променена; предишните сесии са прекратени.");
            }
            else
            {
                user!.EmailConfirmed = true; user.EmailConfirmedAtUtc = now;
                await RevokeAsync(identity, identity.AccountEmailTokens.Where(t => t.UserN == user.N && t.Purpose == purpose), cancellationToken);
                Audit(identity, token, user, "EmailConfirmed", "Имейл адресът е потвърден чрез еднократната връзка.");
            }
            return new(true);
        }, false, cancellationToken);
    }

    public async Task<AccountEmailResult> InviteAsync(ClaimsPrincipal actor, PartnerInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = AccountEmailRules.NormalizeEmail(request.Email);
        if (email == null || request.PartnerN <= 0 || !CustomerDossierVersions.IsRowVersion(request.ExpectedPartnerVersion) ||
            !request.ConfirmedSpecificPartner || !request.ConfirmedCustomerRequest ||
            request.VerificationMethod is not ("in_person" or "known_contact" or "documented_request") || !ValidReason(request.Reason?.Trim()))
            return new(false, "Проверете имейла, точния контрагент, двете потвърждения, начина на проверка и основанието.");
        var site = await readiness.GetPublicSiteUrlAsync(cancellationToken);
        if (site == null) return new(false, StaffEmailUnavailable);
        return await ExecuteAsync(async identity =>
        {
            var staff = await FindStaffAsync(identity, actor, invitations: true, cancellationToken);
            if (staff == null) return new(false, "Нямате актуално право за покани и свързване на акаунти.");
            var partner = await LoadPartnerAsync(request.PartnerN, cancellationToken);
            if (partner == null || !partner.IsActive || Convert.ToBase64String(partner.RowVersion) != request.ExpectedPartnerVersion)
                return new(false, "Контрагентът е променен или не е активен. Презаредете и проверете избора.");
            if (await identity.Users.AnyAsync(u => u.Email == email || u.UserName == email, cancellationToken))
                return new(false, "Имейлът вече има акаунт. Използвайте провереното свързване от клиентското досие.");
            if (!await MayIssueAsync(identity, email, AccountEmailPurpose.PartnerInvitation, cancellationToken)) return new(false, RateLimited);
            partner.AllowAccountLinks = true;
            await shop.SaveChangesAsync(cancellationToken);
            await IssueInvitationAsync(identity, partner, email, staff, request.VerificationMethod!, request.Reason!.Trim(), site, cancellationToken);
            return new(true);
        }, true, cancellationToken);
    }

    public async Task<AccountEmailResult> ResendInvitationAsync(ClaimsPrincipal actor, Guid id, string? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (!CustomerDossierVersions.IsRowVersion(expectedVersion)) return new(false, "Презаредете списъка с покани.");
        var site = await readiness.GetPublicSiteUrlAsync(cancellationToken);
        if (site == null) return new(false, StaffEmailUnavailable);
        return await ExecuteAsync(async identity =>
        {
            var staff = await FindStaffAsync(identity, actor, true, cancellationToken);
            if (staff == null) return new(false, "Нямате актуално право за покани.");
            var token = await LoadTokenAsync(identity, id, cancellationToken);
            if (!EditableInvitation(token, expectedVersion)) return new(false, "Поканата е променена, приета или отменена.");
            var partner = await LoadPartnerAsync(token!.PartnerN!.Value, cancellationToken);
            if (!MatchesPartner(token, partner)) return new(false, "Контрагентът е променен. Направете нова проверка и нова покана.");
            if (await identity.Users.AnyAsync(u => u.Email == token.Email || u.UserName == token.Email, cancellationToken))
                return new(false, "Имейлът вече има акаунт. Използвайте провереното свързване.");
            if (!await MayIssueAsync(identity, token.Email, AccountEmailPurpose.PartnerInvitation, cancellationToken)) return new(false, RateLimited);
            await IssueInvitationAsync(identity, partner!, token.Email, staff, token.VerificationMethod!, token.Reason!, site, cancellationToken);
            return new(true);
        }, true, cancellationToken);
    }

    public Task<AccountEmailResult> CancelInvitationAsync(ClaimsPrincipal actor, Guid id, string? expectedVersion,
        string? reason, CancellationToken cancellationToken = default)
    {
        reason = reason?.Trim();
        if (!CustomerDossierVersions.IsRowVersion(expectedVersion) || !ValidReason(reason))
            return Task.FromResult(new AccountEmailResult(false, "Презаредете поканите и въведете основание до 2000 знака."));
        return ExecuteAsync(async identity =>
        {
            var staff = await FindStaffAsync(identity, actor, true, cancellationToken);
            if (staff == null) return new(false, "Нямате актуално право за покани.");
            var token = await LoadTokenAsync(identity, id, cancellationToken);
            if (!EditableInvitation(token, expectedVersion)) return new(false, "Поканата е променена, приета или отменена.");
            await RevokeAsync(identity, identity.AccountEmailTokens.Where(t => t.Id == id), cancellationToken);
            Audit(identity, token!, staff, "InvitationCancelled", reason!);
            return new(true);
        }, true, cancellationToken);
    }

    public async Task<AccountEmailResult> AcceptInvitationAsync(string value, RegistrationData data,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ValidRegistration(data)) return new(false, "Проверете имената, паролата и приемането на условията.");
            if (!AccountEmailRules.TryParse(value, out var id, out var secret)) return new(false, AccountEmailRules.InvalidLink);
            return await ExecuteAsync(async identity =>
            {
                var token = await LoadTokenAsync(identity, id, cancellationToken);
                if (!ValidToken(token, AccountEmailPurpose.PartnerInvitation, secret)) return new(false, AccountEmailRules.InvalidLink);
                var partner = await LoadPartnerAsync(token!.PartnerN!.Value, cancellationToken);
                if (!MatchesPartner(token, partner)) return new(false, AccountEmailRules.InvalidLink);
                if (await identity.Users.AnyAsync(u => u.Email == token.Email || u.UserName == token.Email, cancellationToken))
                    return new(false, "За този имейл вече има акаунт. Влезте в него и се свържете с магазина за проверено свързване.");
                var user = await CreateUserAsync(identity, token.Email, data, true, cancellationToken);
                token.UserN = user.N; token.ConsumedAtUtc = Now;
                await RevokeAsync(identity, identity.AccountEmailTokens.Where(t => t.Email == token.Email && t.Purpose == AccountEmailPurpose.PartnerInvitation), cancellationToken);
                shop.PartnerAccountLinks.Add(new PartnerAccountLink { UserN = user.N, PartnerN = partner!.N });
                shop.CustomerDossierEvents.Add(new CustomerDossierEvent
                {
                    UserN = user.N, Action = "PartnerLinked", ActorId = user.Id, ActorName = ActorName(user), OccurredAtUtc = Now,
                    Reason = token.Reason!,
                    VerificationMethod = token.VerificationMethod,
                    BeforeJson = JsonSerializer.Serialize(new { PartnerN = (int?)null }),
                    AfterJson = JsonSerializer.Serialize(new { PartnerN = partner.N, partner.Code, Name = partner.SiteName, partner.AllowAccountLinks,
                        InvitationId = token.Id, InvitedById = token.CreatedById, InvitedByName = token.CreatedByName })
                });
                Audit(identity, token, user, "InvitationAccepted", "Клиентът прие поканата и зададе собствена парола.");
                return new(true);
            }, true, cancellationToken);
        }
        finally { data.Password = string.Empty; }
    }

    private async Task IssueForUserAsync(IdentityDbContext identity, ApplicationUser user, AccountEmailPurpose purpose,
        string site, ApplicationUser? actor, string reason, CancellationToken cancellationToken)
    {
        await RevokeAsync(identity, identity.AccountEmailTokens.Where(t => t.UserN == user.N && t.Purpose == purpose), cancellationToken);
        var issued = AccountEmailRules.Issue();
        var token = new AccountEmailToken
        {
            Id = issued.Id, Purpose = purpose, UserN = user.N, Email = user.Email,
            TokenHash = issued.Hash, ExpectedSecurityStamp = user.SecurityStamp, CreatedAtUtc = Now,
            ExpiresAtUtc = purpose == AccountEmailPurpose.PasswordReset ? Now.AddHours(1) : Now.AddHours(24),
            CreatedById = actor?.Id, CreatedByName = actor == null ? null : ActorName(actor), Reason = reason
        };
        identity.AccountEmailTokens.Add(token);
        var reset = purpose == AccountEmailPurpose.PasswordReset;
        var route = reset ? "ResetPassword" : "ConfirmEmail";
        identity.EmailOutboxMessages.Add(outbox.Create(user.Email, reset ? "Нова парола за Алгара" : "Потвърдете имейла си в Алгара",
            reset ? "Поискана е връзка за задаване на нова парола. Тя е валидна 1 час и може да се използва веднъж. Ако не сте я поискали, игнорирайте писмото."
                : "Потвърдете имейл адреса на вашия акаунт. Връзката е валидна 24 часа и може да се използва веднъж.",
            $"{site.TrimEnd('/')}/Account/{route}#{issued.LinkValue}", token.ExpiresAtUtc, token.Id));
        Audit(identity, token, actor, reset ? "PasswordResetRequested" : "EmailConfirmationRequested", reason);
    }

    private async Task IssueInvitationAsync(IdentityDbContext identity, Partner partner, string email,
        ApplicationUser staff, string method, string reason, string site, CancellationToken cancellationToken)
    {
        // Only the newest invitation for an address remains usable, including across partners.
        await RevokeAsync(identity, identity.AccountEmailTokens.Where(t => t.Email == email && t.Purpose == AccountEmailPurpose.PartnerInvitation), cancellationToken);
        var issued = AccountEmailRules.Issue();
        var token = new AccountEmailToken
        {
            Id = issued.Id, Purpose = AccountEmailPurpose.PartnerInvitation, PartnerN = partner.N, Email = email,
            TokenHash = issued.Hash, CreatedAtUtc = Now, ExpiresAtUtc = Now.AddHours(72),
            CreatedById = staff.Id, CreatedByName = ActorName(staff), VerificationMethod = method, Reason = reason,
            PartnerVersion = partner.RowVersion.ToArray()
        };
        identity.AccountEmailTokens.Add(token);
        identity.EmailOutboxMessages.Add(outbox.Create(email, "Покана за клиентски акаунт в Алгара",
            $"По ваше искане изпращаме покана за акаунт, свързан с „{partner.SiteName}“ (код {partner.Code}). Задайте собствена парола и приемете условията. Връзката е валидна 72 часа и може да се използва веднъж. Ако не сте поискали акаунта, игнорирайте писмото.",
            $"{site.TrimEnd('/')}/Account/AcceptInvitation#{issued.LinkValue}", token.ExpiresAtUtc, token.Id));
        Audit(identity, token, staff, "InvitationRequested", reason);
    }

    private async Task RevokeAsync(IdentityDbContext identity, IQueryable<AccountEmailToken> scope, CancellationToken cancellationToken)
    {
        // EF can return a tracked token whose in-memory ConsumedAtUtc has just been set;
        // recheck terminal state after materializing to never mark it both consumed and revoked.
        var tokens = await scope.Where(t => t.RevokedAtUtc == null && t.ConsumedAtUtc == null).ToListAsync(cancellationToken);
        var now = Now;
        foreach (var token in tokens.Where(t => t.RevokedAtUtc == null && t.ConsumedAtUtc == null)) token.RevokedAtUtc = now;
        var ids = tokens.Select(t => t.Id).ToList();
        var messages = await identity.EmailOutboxMessages.Where(m => m.TokenId != null && ids.Contains(m.TokenId.Value) &&
            (m.State == "Pending" || m.State == "Processing")).ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            message.State = "Cancelled"; message.ProtectedPayload = null;
            message.LeaseId = null; message.LeaseUntilUtc = null;
        }
    }

    private async Task<bool> MayIssueAsync(IdentityDbContext identity, string email, AccountEmailPurpose purpose, CancellationToken cancellationToken)
    {
        var since = Now.AddHours(-1); var cooldown = Now.AddMinutes(-1);
        var recent = identity.AccountEmailTokens.Where(t => t.Email == email && t.Purpose == purpose && t.CreatedAtUtc >= since);
        return await recent.CountAsync(cancellationToken) < 5 && !await recent.AnyAsync(t => t.CreatedAtUtc >= cooldown, cancellationToken);
    }

    private async Task<ApplicationUser> CreateUserAsync(IdentityDbContext identity, string email, RegistrationData data,
        bool confirmed, CancellationToken cancellationToken)
    {
        var credential = AccountPasswordCodec.Create(data.Password);
        var name = $"{data.FirstName.Trim()} {data.LastName.Trim()}";
        var user = new ApplicationUser
        {
            Email = email, UserName = email, FirstName = data.FirstName.Trim(), LastName = data.LastName.Trim(),
            FullName = name, DisplayName = name, PhoneNumber = string.IsNullOrWhiteSpace(data.PhoneNumber) ? null : data.PhoneNumber.Trim(),
            PasswordHash = credential.Hash, Salt = credential.Salt, RequireEmailConfirmation = true,
            EmailConfirmed = confirmed, EmailConfirmedAtUtc = confirmed ? Now : null, MarketingConsent = data.MarketingConsent
        };
        identity.Users.Add(user);
        await identity.SaveChangesAsync(cancellationToken);
        var roleN = await identity.Roles.Where(r => r.Name == "User").Select(r => r.N).SingleAsync(cancellationToken);
        identity.UserRoles.Add(new UserRole { UserN = user.N, RoleN = roleN });
        foreach (var (type, granted) in new[] { (ConsentTypes.Terms, true), (ConsentTypes.Privacy, true), (ConsentTypes.Marketing, data.MarketingConsent) })
            identity.UserConsents.Add(new UserConsent { UserN = user.N, ConsentType = type, Granted = granted, PolicyVersion = data.PolicyVersion,
                ConsentedAt = Now, IpAddress = data.IpAddress, UserAgent = data.UserAgent });
        return user;
    }

    private async Task<ApplicationUser?> FindStaffAsync(IdentityDbContext identity, ClaimsPrincipal actor, bool invitations, CancellationToken cancellationToken)
    {
        var access = new StaffAccessService(identity);
        var required = new List<string> { PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier,
            invitations ? PermissionCatalog.Users.ManageInvitations : PermissionCatalog.Users.SendAccountEmails };
        if (invitations) required.AddRange([PermissionCatalog.Users.ManagePartnerLinks, PermissionCatalog.Partners.View]);
        foreach (var permission in required)
            if (!await access.HasPermissionAsync(actor, permission, cancellationToken)) return null;
        var actorId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actorId, cancellationToken);
    }

    private static async Task<bool> MayManageTargetAsync(IdentityDbContext identity, ApplicationUser actor, ApplicationUser target, CancellationToken cancellationToken)
    {
        var roles = StaffRoles.All.ToArray();
        return !await identity.UserRoles.AnyAsync(r => r.UserN == target.N && roles.Contains(r.Role.Name), cancellationToken) ||
            await identity.UserRoles.AnyAsync(r => r.UserN == actor.N && r.Role.Name == StaffRoles.Admin, cancellationToken);
    }

    private async Task<Partner?> LoadPartnerAsync(int n, CancellationToken cancellationToken)
    {
        var partner = await shop.Partners.SingleOrDefaultAsync(p => p.N == n, cancellationToken);
        if (partner != null) await shop.Entry(partner).ReloadAsync(cancellationToken);
        return partner;
    }

    private static Task<ApplicationUser?> LoadUserAsync(IdentityDbContext identity, int n, CancellationToken cancellationToken) =>
        identity.Users.FromSqlInterpolated($"SELECT * FROM [Users] WITH (UPDLOCK,HOLDLOCK) WHERE [N]={n}").SingleOrDefaultAsync(cancellationToken);

    private static Task<AccountEmailToken?> LoadTokenAsync(IdentityDbContext identity, Guid id, CancellationToken cancellationToken) =>
        identity.AccountEmailTokens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    private bool ValidToken(AccountEmailToken? token, AccountEmailPurpose purpose, byte[] secret) =>
        token != null && token.Purpose == purpose && token.ConsumedAtUtc == null && token.RevokedAtUtc == null &&
        token.ExpiresAtUtc > Now && AccountEmailRules.MatchesHash(secret, token.TokenHash);

    private static bool MatchesUser(AccountEmailToken token, ApplicationUser? user) =>
        user != null && user.IsActive && user.SecurityStamp == token.ExpectedSecurityStamp &&
        string.Equals(user.Email, token.Email, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPartner(AccountEmailToken token, Partner? partner) =>
        partner != null && partner.IsActive && partner.AllowAccountLinks && token.PartnerVersion != null &&
        partner.RowVersion.AsSpan().SequenceEqual(token.PartnerVersion);

    private static bool EditableInvitation(AccountEmailToken? token, string? version) =>
        token?.Purpose == AccountEmailPurpose.PartnerInvitation && token.ConsumedAtUtc == null && token.RevokedAtUtc == null &&
        Convert.ToBase64String(token.RowVersion) == version;

    private static bool ValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 2000;

    private static bool ValidRegistration(RegistrationData data) => data.TermsAccepted && AccountEmailRules.ValidPassword(data.Password) &&
        data.FirstName?.Trim().Length is >= 2 and <= 60 && data.LastName?.Trim().Length is >= 2 and <= 60 &&
        (data.PhoneNumber?.Length ?? 0) <= 20;

    private static string ActorName(ApplicationUser user) =>
        (string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName) is var name && name.Length > 256 ? name[..256] : name;

    private void Audit(IdentityDbContext identity, AccountEmailToken token, ApplicationUser? actor, string action, string reason) =>
        identity.AccountEmailAuditEvents.Add(new AccountEmailAuditEvent { TokenId = token.Id, UserN = token.UserN, PartnerN = token.PartnerN,
            ActorId = actor?.Id ?? "public", ActorName = actor == null ? "Клиентска заявка" : ActorName(actor),
            Action = action, Reason = reason, OccurredAtUtc = Now });

    private async Task<AccountEmailResult> ExecuteAsync(Func<IdentityDbContext, Task<AccountEmailResult>> action,
        bool partnerLock, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await shop.Database.BeginTransactionAsync(cancellationToken);
            await shop.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource=N'Algara.AccessManagement',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
                IF @result<0 THROW 51004,'Access management is busy.',1;
                """, cancellationToken);
            if (partnerLock) await shop.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource=N'Algara.PartnerCatalog',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
                IF @result<0 THROW 51004,'Partner catalog is busy.',1;
                """, cancellationToken);
            await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(shop.Database.GetDbConnection()).Options);
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            var result = await action(identity);
            if (!result.Success) { shop.ChangeTracker.Clear(); return result; }
            await identity.SaveChangesAsync(cancellationToken);
            await shop.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException)
        {
            shop.ChangeTracker.Clear();
            return new(false, "Промяната не е записана. Данните са променени или адресът вече е зает. Презаредете и опитайте отново.");
        }
        catch (CryptographicException)
        {
            shop.ChangeTracker.Clear();
            return new(false, Unavailable);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004)
        {
            shop.ChangeTracker.Clear();
            return new(false, "Друго действие обработва данните. Опитайте отново.");
        }
    }
}

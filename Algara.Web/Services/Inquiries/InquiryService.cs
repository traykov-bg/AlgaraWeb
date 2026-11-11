using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Web.Services.Checkout;
using Algara.Web.Services.Email;
using Algara.Web.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services.Inquiries;

public sealed class InquiryService(ShopDbContext db, CheckoutTokens tokens, InquiryFiles files,
    EmailOutboxFactory outbox, ISmtpSecretProtector smtpSecrets, TimeProvider clock)
{
    public static string Owner(ClaimsPrincipal actor, bool staff) =>
        "inquiry:" + staff + ":" + CheckoutService.Owner(actor) + ":" + actor.FindFirstValue("SessionId");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static bool Valid(InquiryForm form) =>
        Validator.TryValidateObject(form, new ValidationContext(form), null, true) &&
        !string.IsNullOrWhiteSpace(form.ContactName) && !string.IsNullOrWhiteSpace(form.Phone) &&
        !form.ContactName.Any(char.IsControl) && !form.Phone.Any(char.IsControl) &&
        !string.IsNullOrWhiteSpace(form.Description) &&
        (string.IsNullOrWhiteSpace(form.Email) || AccountEmailRules.NormalizeEmail(form.Email) != null) &&
        (form.Staff ? form.Channel is "Phone" or "InStore" && !string.IsNullOrWhiteSpace(form.Reason) : form.Consent);

    public static bool HasGuestAccess(FurnitureInquiry inquiry, string? token) =>
        AccountEmailRules.TryParse(token, out var id, out var secret) && id == inquiry.SubmissionId &&
        AccountEmailRules.MatchesHash(secret, inquiry.GuestTokenHash);

    private async Task<ApplicationUser?> CustomerAsync(IdentityDbContext identity, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (actor.Identity?.IsAuthenticated != true) return null;
        var id = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await identity.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (user == null || (user.RequireEmailConfirmation && !user.EmailConfirmed) ||
            actor.FindFirstValue("SecurityStamp") != user.SecurityStamp ||
            !await identity.UserSessions.AsNoTracking().AnyAsync(x => x.UserN == user.N && x.IsActive &&
                x.SessionId == actor.FindFirstValue("SessionId"), ct)) return null;
        return user;
    }

    private IdentityDbContext Identity() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(db.Database.GetDbConnection()).Options);

    public async Task<string?> CurrentCustomerIdAsync(ClaimsPrincipal actor, CancellationToken ct)
    {
        await using var identity = Identity();
        return (await CustomerAsync(identity, actor, ct))?.Id;
    }

    private Task LockAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        DECLARE @r int;
        EXEC @r=sys.sp_getapplock @Resource=N'Algara.AccessManagement',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
        IF @r<0 THROW 51004,'Inquiry busy.',1;
        """, ct);

    public async Task<FurnitureInquiry?> CreateAsync(InquiryForm form, ClaimsPrincipal actor, string language, CancellationToken ct)
    {
        var ticket = tokens.Read(form.SubmissionToken, Owner(actor, form.Staff));
        if (ticket == null) { form.Error = "Expired"; return null; }
        if (!Valid(form)) { form.Error = "Invalid"; return null; }
        try
        {
            var attachments = await files.PrepareAsync(form.Files, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(ct);
            await using var identity = Identity();
            await identity.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            var customer = await CustomerAsync(identity, actor, ct);
            if (actor.Identity?.IsAuthenticated == true && customer == null) { form.Error = "AccessDenied"; return null; }
            var access = new StaffAccessService(identity);
            if (form.Staff && (!await access.HasPermissionAsync(actor, PermissionCatalog.Inquiries.View, ct) ||
                !await access.HasPermissionAsync(actor, PermissionCatalog.Inquiries.Create, ct))) { form.Error = "AccessDenied"; return null; }
            var existing = await db.Set<FurnitureInquiry>().AsNoTracking().SingleOrDefaultAsync(x => x.SubmissionId == ticket.Id, ct);
            if (existing != null) return existing;
            var product = form.ProductN.HasValue ? await db.Products.AsNoTracking().Include(x => x.Translations).Include(x => x.FabricSurcharges).Include(x => x.Variants).ThenInclude(x => x.Color).ThenInclude(x => x.Fabric).SingleOrDefaultAsync(x => x.N == form.ProductN && x.IsActive && !x.IsStaffOnly, ct) : null;
            if (form.ProductN.HasValue && product == null) { form.Error = "ProductUnavailable"; return null; }
            var selection = product == null ? null : InquiryProductSelection.Describe(product, form.VariantN, form.Quantity, language);
            if ((product != null && selection == null) || (product == null && form.VariantN != null)) { form.Error = "ProductUnavailable"; return null; }
            var description = selection == null ? form.Description.Trim() : selection + "\n\n" + form.Description.Trim();
            if (description.Length > 4000) { form.Error = "Invalid"; return null; }
            var email = AccountEmailRules.NormalizeEmail(form.Email);
            var site = email == null ? null : await new EmailDeliveryReadiness(identity, smtpSecrets).GetPublicSiteUrlAsync(ct);
            if (email != null && site == null) { form.Error = "EmailUnavailable"; return null; }
            AccountEmailRules.TryParse(ticket.GuestSecret, out _, out var secret);
            var inquiry = new FurnitureInquiry {
                SubmissionId = ticket.Id, GuestTokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(secret)),
                CustomerId = form.Staff ? null : customer?.Id, CreatedByStaffId = form.Staff ? customer!.Id : null,
                Channel = form.Staff ? form.Channel : "Web", ContactName = form.ContactName.Trim(), Phone = form.Phone.Trim(), Email = email,
                ProductN = product?.N, VariantN = form.VariantN, Quantity = product == null ? null : form.Quantity, ProductName = product?.Name, Dimensions = form.Dimensions?.Trim() ?? "",
                Materials = form.Materials?.Trim() ?? "", Description = description, Language = language == "en" ? "en" : "bg",
                CreatedAtUtc = Now, UpdatedAtUtc = Now, Attachments = attachments
            };
            inquiry.Events.Add(Event(inquiry, customer, "Created", form.Staff ? form.Reason! : "PrivacyAcknowledged"));
            db.Add(inquiry);
            if (email != null) identity.EmailOutboxMessages.Add(outbox.Create(email,
                inquiry.Language == "en" ? "Furniture inquiry received" : "Получено запитване за мебел",
                inquiry.Language == "en" ? "Your inquiry has been received. Keep this private link to view its offer. An inquiry is not an order." :
                "Получихме Вашето запитване. Запазете личната връзка за преглед на офертата. Запитването не е поръчка.",
                site + "/inquiries/access#" + ticket.GuestSecret, null, null));
            await db.SaveChangesAsync(ct); await identity.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return inquiry;
        }
        catch (ProductImageValidationException) { form.Error = "Files"; return null; }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); form.Error = "Retry"; return null; }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear(); form.Error = "Retry"; return null; }
    }

    public async Task<bool> CanReadAsync(FurnitureInquiry inquiry, ClaimsPrincipal actor, string? guest, bool staff, CancellationToken ct)
    {
        await using var identity = Identity();
        if (staff) return await new StaffAccessService(identity).HasPermissionAsync(actor, PermissionCatalog.Inquiries.View, ct);
        // An authenticated different account cannot inherit anonymous browser access.
        if (actor.Identity?.IsAuthenticated == true)
        {
            var customer = await CustomerAsync(identity, actor, ct);
            return customer != null && inquiry.CustomerId == customer.Id;
        }
        return inquiry.CustomerId == null && HasGuestAccess(inquiry, guest);
    }

    public static InquiryStatus? Next(InquiryStatus status, string command, bool staff) => (status, command, staff) switch
    {
        (InquiryStatus.New, "Assign" or "Note", true) => InquiryStatus.InProgress,
        (InquiryStatus.New or InquiryStatus.InProgress or InquiryStatus.Offered or InquiryStatus.Accepted, "Offer", true) => InquiryStatus.Offered,
        (InquiryStatus.Offered, "Accept", _) => InquiryStatus.Accepted,
        (InquiryStatus.New or InquiryStatus.InProgress or InquiryStatus.Offered, "Complete", true) => InquiryStatus.Accepted,
        (InquiryStatus.New or InquiryStatus.InProgress or InquiryStatus.Offered, "Decline", _) => InquiryStatus.Declined,
        (InquiryStatus.Declined, "Close", true) => InquiryStatus.Closed,
        (_, "Note" or "Assign", true) => status,
        _ => null
    };

    public async Task<string?> UpdateAsync(InquiryUpdate form, ClaimsPrincipal actor, string? guest, bool staff, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(ct);
            await using var identity = Identity(); await identity.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            var access = new StaffAccessService(identity);
            var customer = await CustomerAsync(identity, actor, ct);
            if (staff && (!await access.HasPermissionAsync(actor, PermissionCatalog.Inquiries.View, ct) ||
                !await access.HasPermissionAsync(actor, PermissionCatalog.Inquiries.Manage, ct))) return "AccessDenied";
            db.ChangeTracker.Clear();
            var inquiry = await db.Set<FurnitureInquiry>().Include(x => x.Offers).SingleOrDefaultAsync(x => x.N == form.N, ct);
            if (inquiry == null) return "AccessDenied";
            if (!staff && (actor.Identity?.IsAuthenticated == true ? customer == null || inquiry.CustomerId != customer.Id :
                inquiry.CustomerId != null || !HasGuestAccess(inquiry, guest))) return "AccessDenied";
            if (Convert.ToBase64String(inquiry.RowVersion) != form.Version) return "Changed";
            var status = Next(inquiry.Status, form.Command, staff);
            if (status == null || (staff && string.IsNullOrWhiteSpace(form.Note))) return "Invalid";
            var createsOffer = form.Command == "Offer" || (form.Command == "Complete" && inquiry.Offers.Count == 0);
            var validation = createsOffer ? form : new InquiryUpdate { N = form.N, Version = form.Version, Command = form.Command, Note = form.Note, Agreement = form.Agreement };
            if (!Validator.TryValidateObject(validation, new ValidationContext(validation), null, true)) return "Invalid";
            if (form.Command is "Accept" or "Complete" && !form.Agreement) return "Agreement";
            if (form.Command == "Accept" && inquiry.Offers.Count == 0) return "Invalid";
            InquiryOffer? recordedOffer = null;
            if (createsOffer)
            {
                foreach (var permission in new[] { PermissionCatalog.Orders.View, PermissionCatalog.Orders.Create, PermissionCatalog.Partners.View })
                    if (!await access.HasPermissionAsync(actor, permission, ct)) return "AccessDenied";
                recordedOffer = await InquiryOfferBuilder.BuildAsync(db, form, customer!.Id, Now);
                if (recordedOffer == null) return "OfferLinesInvalid";
                inquiry.Offers.Add(recordedOffer);
                if (form.Command == "Offer" && inquiry.Email != null)
                {
                    var site = await new EmailDeliveryReadiness(identity, smtpSecrets).GetPublicSiteUrlAsync(ct);
                    if (site == null) return "EmailUnavailable";
                    identity.EmailOutboxMessages.Add(outbox.Create(inquiry.Email,
                        inquiry.Language == "en" ? "Furniture inquiry: new offer" : "Запитване за мебел: нова оферта",
                        inquiry.Language == "en" ? "A new offer is available. Use your original private inquiry link to review its price, timeframe and terms." :
                        "Има нова оферта. Използвайте първоначалната лична връзка към запитването, за да прегледате цената, срока и условията.", null, null, null));
                }
            }
            if (form.Command == "Assign") inquiry.AssignedStaffId = customer!.Id;
            else if (form.Command is "Offer" or "Complete") inquiry.AssignedStaffId ??= customer!.Id;
            inquiry.Status = status.Value; inquiry.UpdatedAtUtc = Now;
            var note = staff ? form.Note! : "";
            // Save within the same transaction to obtain a new phone-agreed offer's identity for its audit.
            if (recordedOffer != null && form.Command == "Complete") await db.SaveChangesAsync(ct);
            if (form.Command is "Accept" or "Complete") note = $"Offer #{inquiry.Offers.Max(x => x.N)}; " + note;
            // Keep the audit within its database bound even when adding the accepted offer identity.
            if (note.Length > 2000) return "Invalid";
            db.Add(Event(inquiry, customer, form.Command, note));
            await db.SaveChangesAsync(ct); await identity.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return null;
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return "Changed"; }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear(); return "Retry"; }
    }

    private InquiryEvent Event(FurnitureInquiry inquiry, ApplicationUser? actor, string action, string note) => new() {
        InquiryN = inquiry.N, ActorId = actor?.Id,
        ActorName = (actor == null ? inquiry.ContactName : string.Join(" ", new[] { actor.FirstName, actor.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)))) is { Length: > 0 } name ? name[..Math.Min(name.Length, 200)] : "Account",
        AtUtc = Now, Status = inquiry.Status, Action = action, Note = note
    };
}

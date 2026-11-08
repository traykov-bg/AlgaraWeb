using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Data;
using Algara.Identity.Services;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Email;
using Algara.Web.Services.Shipping;
using Algara.Web.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services.Checkout;

public sealed record CheckoutResult(Order? Order = null, string? GuestSecret = null, string? ErrorKey = null)
{
    public override string ToString() => $"CheckoutResult: {Order?.N}, {ErrorKey}";
}

public sealed class CheckoutService(ShopDbContext db, CartService cart, DeliveryQuoteService delivery,
    CheckoutTokens tokens, EmailDeliveryReadiness readiness, EmailOutboxFactory outbox, TimeProvider clock)
{
    public static string Owner(ClaimsPrincipal actor) => actor.Identity?.IsAuthenticated == true ? actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? "invalid" : "guest";

    public async Task<CheckoutResult> SubmitAsync(CheckoutForm form, ClaimsPrincipal actor, CancellationToken ct)
    {
        try { return await SubmitCoreAsync(form, actor, ct); }
        finally { form.Password = form.ConfirmPassword = null; }
    }

    private async Task<CheckoutResult> SubmitCoreAsync(CheckoutForm form, ClaimsPrincipal actor, CancellationToken ct)
    {
        var ticket = tokens.Read(form.SubmissionToken, Owner(actor));
        if (ticket == null) return new(ErrorKey: "Expired");
        if (!Validator.TryValidateObject(form, new ValidationContext(form), null, true) || !form.TermsAccepted || AccountEmailRules.NormalizeEmail(form.Email) == null ||
            string.IsNullOrWhiteSpace(form.FirstName) || string.IsNullOrWhiteSpace(form.LastName) || string.IsNullOrWhiteSpace(form.Phone) ||
            (form.InvoiceRequested && string.IsNullOrWhiteSpace(form.InvoiceDetails))) return new(ErrorKey: "InvalidDetails");
        if (form.Method == DeliveryMethod.OwnTransport && (string.IsNullOrWhiteSpace(form.Address) || form.Address.Any(char.IsControl)))
            return new(ErrorKey: "InvalidDetails");
        if (form.CreateAccount && (Owner(actor) != "guest" || !AccountEmailRules.ValidPassword(form.Password) || form.Password != form.ConfirmPassword))
            return new(ErrorKey: "InvalidPassword");
        var accepted = await db.Orders.AsNoTracking().Include(o => o.OrderItems).SingleOrDefaultAsync(o => o.SubmissionId == ticket.Id, ct);
        if (accepted != null) return new(accepted, ticket.GuestSecret);
        var site = await readiness.GetPublicSiteUrlAsync(ct);
        if (site == null) return new(ErrorKey: "EmailUnavailable");
        var preview = await cart.GetAsync();
        var result = await delivery.CalculateAsync(preview, form.Method, form.City, form.PostalCode, form.Address, form.OfficeCode, ct);
        if (result.Quote == null) return new(ErrorKey: result.ErrorKey);
        if (result.Quote.IsTest) return new(ErrorKey: "TestNotOrder");
        var fingerprint = CheckoutTokens.Fingerprint(preview, form, result.Quote);
        if (!tokens.Matches(form.QuoteToken, ticket, fingerprint)) return new(ErrorKey: "Changed");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @r int;
                EXEC @r=sys.sp_getapplock @Resource=N'Algara.AccessManagement',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
                IF @r<0 THROW 51004,'Checkout busy.',1;
                EXEC @r=sys.sp_getapplock @Resource=N'Algara:ProductCodes',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
                IF @r<0 THROW 51004,'Checkout busy.',1;
                """, ct);
            db.ChangeTracker.Clear();
            var existing = await db.Orders.Include(o => o.OrderItems).SingleOrDefaultAsync(o => o.SubmissionId == ticket.Id, ct);
            if (existing != null) return new(existing, ticket.GuestSecret);
            await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(db.Database.GetDbConnection()).Options);
            await identity.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            int? userN = null;
            if (Owner(actor) != "guest")
            {
                var user = await identity.Users.SingleOrDefaultAsync(u => u.Id == Owner(actor), ct);
                if (user == null || !user.IsActive || user.SecurityStamp != actor.FindFirstValue("SecurityStamp") ||
                    (user.RequireEmailConfirmation && !user.EmailConfirmed) ||
                    !await identity.UserSessions.AnyAsync(s => s.UserN == user.N && s.SessionId == actor.FindFirstValue("SessionId") && s.IsActive, ct)) return new(ErrorKey: "Expired");
                userN = user.N;
            }
            var snapshot = await cart.GetAsync();
            // Recheck all quote inputs inside the inventory transaction, without network waits under locks.
            if (!snapshot.CanQuote || fingerprint != CheckoutTokens.Fingerprint(snapshot, form, result.Quote)) return new(ErrorKey: "Changed");
            if (form.CreateAccount)
            {
                var account = await new AccountEmailService(db, outbox, readiness, clock).RegisterCheckoutAsync(identity,
                    new RegistrationData { Email = form.Email, Password = form.Password!, FirstName = form.FirstName, LastName = form.LastName,
                        PhoneNumber = form.Phone, TermsAccepted = true }, site, ct);
                if (account == null) return new(ErrorKey: "AccountExists");
                userN = account.N;
            }
            AccountEmailRules.TryParse(ticket.GuestSecret, out _, out var secret);
            var now = clock.GetUtcNow().UtcDateTime;
            var order = new Order
            {
                UserN = userN, SubmissionId = ticket.Id, GuestTokenHash = Convert.ToHexString(SHA256.HashData(secret)),
                ContactName = form.FirstName.Trim() + " " + form.LastName.Trim(), ContactEmail = AccountEmailRules.NormalizeEmail(form.Email), ContactPhone = form.Phone.Trim(),
                Comment = form.Comment?.Trim(), InvoiceDetails = form.InvoiceRequested ? form.InvoiceDetails?.Trim() : null,
                CreatedAt = now, ExpiresAtUtc = now.AddDays(7), TermsAcceptedAtUtc = now,
                DeliveryAmount = result.Quote.DeliveryAmount, TotalAmount = result.Quote.Total,
                DeliverySnapshot = JsonSerializer.Serialize(new
                {
                    form.Method, Currency = "EUR", Payment = "OnReceipt", TermsPolicyVersion = "1.0",
                    City = form.Method == DeliveryMethod.Pickup ? null : form.City?.Trim(),
                    PostalCode = form.Method == DeliveryMethod.Pickup || DeliveryQuoteService.IsOffice(form.Method) ? null : form.PostalCode?.Trim(),
                    Address = form.Method == DeliveryMethod.Pickup || DeliveryQuoteService.IsOffice(form.Method) ? null : form.Address?.Trim(),
                    AddressDetails = form.Method == DeliveryMethod.Pickup || DeliveryQuoteService.IsOffice(form.Method) ? null : form.AddressDetails?.Trim(),
                    result.Quote.Office, result.Quote.ResolvedAddress, result.Quote.DistanceKm
                }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) })
            };
            foreach (var line in snapshot.Lines)
            {
                var stock = line.Variant?.StockQuantity ?? line.Product!.StockQuantity ?? 0;
                var reserved = await new StockReservationReader(db, clock).ReservedAsync(line.Entry.ProductN, line.Entry.VariantN);
                var quantity = Math.Min(line.Entry.Quantity, Math.Max(0, stock - reserved));
                order.OrderItems.Add(new OrderItem { ProductN = line.Entry.ProductN, VariantN = line.Entry.VariantN, Quantity = line.Entry.Quantity,
                    UnitPrice = line.UnitPrice!.Value, ProductName = line.Name, VariantName = line.VariantName, Sku = line.Sku,
                    ReservedQuantity = quantity, RequiresManufacturing = quantity < line.Entry.Quantity });
            }
            db.Orders.Add(order);
            db.Set<OrderEvent>().Add(new OrderEvent { Order = order, Action = "Submitted", ActorId = Owner(actor), Status = OrderStatus.Pending, OccurredAtUtc = now });
            await db.SaveChangesAsync(ct);
            identity.EmailOutboxMessages.Add(outbox.Create(order.ContactEmail!, "Algara / Алгара — #" + order.N,
                "Поръчката е получена и очаква потвърждение. / Your order was received and awaits confirmation.",
                site + "/Checkout/Access#" + ticket.GuestSecret, null, null));
            await identity.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(order, ticket.GuestSecret);
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(ErrorKey: "Retry"); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear(); return new(ErrorKey: "Retry"); }
        finally { form.Password = form.ConfirmPassword = null; }
    }
}

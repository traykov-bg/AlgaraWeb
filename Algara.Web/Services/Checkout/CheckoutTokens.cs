using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Email;
using Algara.Web.Services.Shipping;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.Web.Services.Checkout;

public sealed record CheckoutTicket(Guid Id, string GuestSecret, string Owner, DateTimeOffset Expires)
{
    public override string ToString() => nameof(CheckoutTicket);
}
public sealed record QuoteTicket(Guid Submission, string Fingerprint, DateTimeOffset Expires);

public sealed class CheckoutTokens(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector submissions = protection.CreateProtector("Algara.Checkout.Submission.v1");
    private readonly IDataProtector quotes = protection.CreateProtector("Algara.Checkout.Quote.v1");
    public string Issue(string owner)
    {
        var guest = AccountEmailRules.Issue();
        return submissions.Protect(JsonSerializer.Serialize(new CheckoutTicket(guest.Id, guest.LinkValue, owner, clock.GetUtcNow().AddHours(24))));
    }
    public CheckoutTicket? Read(string? value, string owner)
    {
        var ticket = Read<CheckoutTicket>(submissions, value);
        return ticket?.Owner == owner && ticket.Expires > clock.GetUtcNow() ? ticket : null;
    }
    public string Quote(CheckoutTicket ticket, string fingerprint) => quotes.Protect(JsonSerializer.Serialize(
        new QuoteTicket(ticket.Id, fingerprint, clock.GetUtcNow().AddMinutes(15))));
    public bool Matches(string? value, CheckoutTicket ticket, string fingerprint)
    {
        var quote = Read<QuoteTicket>(quotes, value);
        return quote?.Submission == ticket.Id && quote.Expires > clock.GetUtcNow() && quote.Fingerprint == fingerprint;
    }
    public static string Fingerprint(CartSnapshot cart, CheckoutForm form, DeliveryQuote quote) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Lines = cart.Lines.Select(l => new { l.Entry, l.UnitPrice, l.Name, l.VariantName, l.Sku, l.Availability, l.Product!.AllowsCourier, l.Product.UseCatalogDimensionsForShipping, l.Product.WidthCm, l.Product.HeightCm, l.Product.DepthCm,
                Parcels = l.Product.ShippingParcels.OrderBy(p => p.Position).Select(p => new { p.WeightKg, p.WidthCm, p.HeightCm, p.DepthCm }) }),
            form.Method, City = form.City?.Trim(), PostalCode = form.PostalCode?.Trim(), Address = form.Address?.Trim(), OfficeCode = form.OfficeCode?.Trim(),
            quote.GoodsAmount, quote.DeliveryAmount, quote.ResolvedAddress, quote.Office, quote.IsTest
        }))));
    private static T? Read<T>(IDataProtector protector, string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 10000) return default;
        try { return JsonSerializer.Deserialize<T>(protector.Unprotect(value)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return default; }
    }
}

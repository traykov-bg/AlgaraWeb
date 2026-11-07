using Algara.Data.Models;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Checkout;
using Algara.Web.Services.Shipping;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.UnitTests.Services;

public class CheckoutTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void SubmissionCannotBeForgedOrMovedBetweenAccounts()
    {
        var tokens = new CheckoutTokens(new EphemeralDataProtectionProvider(), new Clock());
        var value = tokens.Issue("alice");
        Assert.NotNull(tokens.Read(value, "alice"));
        Assert.Null(tokens.Read(value, "bob"));
        Assert.Null(tokens.Read(value, "guest"));
        Assert.Null(tokens.Read("x" + value, "alice"));
        Assert.Null(tokens.Read(new string('x', 10001), "alice"));
        Assert.DoesNotContain(tokens.Read(value, "alice")!.GuestSecret, tokens.Read(value, "alice")!.ToString());
    }

    [Fact]
    public void QuotesExpireAndCannotBeMovedBetweenSubmissions()
    {
        var clock = new Clock();
        var tokens = new CheckoutTokens(new EphemeralDataProtectionProvider(), clock);
        var first = tokens.Read(tokens.Issue("guest"), "guest")!;
        var second = tokens.Read(tokens.Issue("guest"), "guest")!;
        var quote = tokens.Quote(first, "price-v1");
        Assert.True(tokens.Matches(quote, first, "price-v1"));
        Assert.False(tokens.Matches(quote, second, "price-v1"));
        Assert.False(tokens.Matches(quote, first, "price-v2"));
        clock.Now += TimeSpan.FromMinutes(15);
        Assert.False(tokens.Matches(quote, first, "price-v1"));
        var submission = tokens.Issue("guest");
        clock.Now += TimeSpan.FromHours(24);
        Assert.Null(tokens.Read(submission, "guest"));
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("eligibility")]
    [InlineData("packaging")]
    [InlineData("destination")]
    [InlineData("delivery")]
    public void ChangedOrderInputsRequireFreshConsent(string change)
    {
        var product = new Product { N = 1, Name = "Chair", Price = 20, IsActive = true, StockQuantity = 10, AllowsCourier = true };
        product.ShippingParcels.Add(new ProductShippingParcel { N = 1, WeightKg = 1, WidthCm = 10, HeightCm = 10, DepthCm = 10 });
        var form = new CheckoutForm { Method = DeliveryMethod.EcontAddress, City = "Plovdiv", Address = "Street 1", PostalCode = "4000" };
        var quantity = 1;
        CartSnapshot Cart() => new() { Lines = [CartService.Evaluate(new(1, null, quantity), product, DateTime.Now)] };
        var quote = new DeliveryQuote(20, 5, DateTimeOffset.UtcNow);
        var before = CheckoutTokens.Fingerprint(Cart(), form, quote);
        switch (change)
        {
            case "quantity": quantity = 2; break;
            case "price": product.Price = 21; break;
            case "eligibility": product.AllowsCourier = false; break;
            case "packaging": product.ShippingParcels.Single().WeightKg = 2; break;
            case "destination": form.Address = "Street 2"; break;
            case "delivery": quote = quote with { DeliveryAmount = 6 }; break;
        }
        Assert.NotEqual(before, CheckoutTokens.Fingerprint(Cart(), form, quote));
    }

    [Theory]
    [InlineData(false, 4, 3, 2, CartLineIssue.InsufficientStock)]
    [InlineData(false, 4, 4, 1, CartLineIssue.InquiryRequired)]
    [InlineData(false, 4, 2, 2, CartLineIssue.None)]
    [InlineData(true, 4, 4, 2, CartLineIssue.None)]
    public void ReservationsReduceSellableStockWithoutMutatingPhysicalStock(bool upholstered, int physical, int reserved, int quantity, CartLineIssue expected)
    {
        var product = new Product { N = 1, Name = "Chair", Price = 20, IsActive = true, StockQuantity = physical, IsUpholstered = upholstered };
        var line = CartService.Evaluate(new(1, null, quantity), product, DateTime.Now, reserved);
        Assert.Equal(expected, line.Issue);
        Assert.Equal(physical, product.StockQuantity);
        if (upholstered) Assert.Equal(ProductAvailability.MadeToOrder, line.Availability);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    public void LifecycleCannotBypassConfirmationOrReopenReleasedStock(OrderStatus from, OrderStatus to)
        => Assert.False(OrderLifecycleService.Allows(from, to));
}

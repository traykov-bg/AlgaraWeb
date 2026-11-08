using Algara.Data.Models;
using Algara.Web.Services.Checkout;
using Algara.Web.Services.Email;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.UnitTests.Services;

public class OrderStatusEmailTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, false)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Cancelled, true)]
    public void OnlyRequestedStatusesProduceProtectedEmail(OrderStatus status, bool expected)
    {
        var factory = new EmailOutboxFactory(new EphemeralDataProtectionProvider());
        var message = OrderStatusEmail.Create(factory, new Order { N = 42, ContactEmail = "buyer@example.test", Status = status });
        Assert.Equal(expected, message != null);
        if (message == null) return;
        Assert.Equal("buyer@example.test", message.Recipient);
        Assert.Contains("42", message.Subject);
        Assert.True(factory.TryReadPayload(message, out var payload));
        Assert.Null(payload.ActionUrl);
        Assert.False(string.IsNullOrWhiteSpace(payload.Text));
    }

    [Fact]
    public void SandboxSnapshotMarksLaterStatusEmailsAsTest()
    {
        var factory = new EmailOutboxFactory(new EphemeralDataProtectionProvider());
        var order = new Order { N = 42, ContactEmail = "buyer@example.test", Status = OrderStatus.Shipped, DeliverySnapshot = "{\"IsTest\":true}" };
        var message = OrderStatusEmail.Create(factory, order)!;
        Assert.StartsWith("[TEST]", message.Subject);
        Assert.True(factory.TryReadPayload(message, out var payload));Assert.Contains("Test courier quote", payload.Text);
    }

    [Fact]
    public void ShipmentEmailIncludesPreviouslySavedWaybill()
    {
        var factory = new EmailOutboxFactory(new EphemeralDataProtectionProvider());
        var order = new Order { N = 42, ContactEmail = "buyer@example.test", Status = OrderStatus.Shipped, WaybillNumber = "WB-123456" };
        var message = OrderStatusEmail.Create(factory, order)!;
        Assert.True(factory.TryReadPayload(message, out var payload));
        Assert.Contains("WB-123456", payload.Text);
    }

    [Fact]
    public void ExpirationExplainsReasonAndLegacyMissingEmailDoesNotBlockStatus()
    {
        var factory = new EmailOutboxFactory(new EphemeralDataProtectionProvider());
        var order = new Order { N = 42, ContactEmail = "buyer@example.test", Status = OrderStatus.Cancelled };
        var message = OrderStatusEmail.Create(factory, order, expired: true)!;
        Assert.True(factory.TryReadPayload(message, out var payload));
        Assert.Contains("7 days", payload.Text);
        order.ContactEmail = null;
        Assert.Null(OrderStatusEmail.Create(factory, order));
    }
}

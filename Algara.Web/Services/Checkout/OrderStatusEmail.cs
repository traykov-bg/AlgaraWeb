using System.Text.Json;
using Algara.Data.Models;
using Algara.Identity.Models;
using Algara.Web.Services.Email;

namespace Algara.Web.Services.Checkout;

public static class OrderStatusEmail
{
    public static EmailOutboxMessage? Create(EmailOutboxFactory outbox, Order order, bool expired = false)
    {
        if (!EmailOutboxFactory.IsMailbox(order.ContactEmail)) return null;
        var text = order.Status switch
        {
            OrderStatus.Confirmed => "Поръчката е потвърдена. / Your order has been confirmed.",
            OrderStatus.Shipped => "Поръчката е изпратена. / Your order has been dispatched.",
            OrderStatus.Cancelled when expired => "Поръчката е анулирана, защото не беше потвърдена в рамките на 7 дни. / Your order was cancelled because it was not confirmed within 7 days.",
            OrderStatus.Cancelled => "Поръчката е анулирана. / Your order has been cancelled.",
            _ => null
        };
        if (text != null && order.Status == OrderStatus.Shipped && !string.IsNullOrEmpty(order.WaybillNumber))
            text += " Товарителница / Waybill: " + order.WaybillNumber;
        if (text == null) return null;
        var test = false;
        if (order.DeliverySnapshot != null)
        {
            try
            {
                using var snapshot = JsonDocument.Parse(order.DeliverySnapshot);
                test = snapshot.RootElement.ValueKind == JsonValueKind.Object && snapshot.RootElement.TryGetProperty("IsTest", out var value) && value.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { /* Legacy snapshots must not prevent a status notification. */ }
        }
        if (test && text != null) text = "Тестова куриерска калкулация. / Test courier quote. " + text;
        return text == null ? null : outbox.Create(order.ContactEmail!, (test ? "[TEST] " : "") + "Algara / Алгара — #" + order.N,
            text, null, null, null);
    }
}

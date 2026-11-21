using System.Globalization;
using Algara.Data.Models;
using Algara.Web.Services.Checkout;

namespace Algara.Web.Services.Payments;

public sealed record OrderStatusPaymentNotice(string Message, string ReasonLabel)
{
    public static OrderStatusPaymentNotice? Create(Order order, OrderStatus target, decimal paid,
        bool exempt, int deferredDays, bool canOverride)
    {
        if (!OrderLifecycleService.Allows(order.Status, target)) return null;
        var culture = CultureInfo.GetCultureInfo("bg-BG");
        string Money(decimal amount) => amount.ToString("N2", culture) + " €";
        string message;
        string label;
        if (target == OrderStatus.Confirmed)
        {
            var deposit = PaymentRules.Deposit(order, exempt);
            if (paid >= deposit) return null;
            message = $"За потвърждаване на поръчката трябва да са отчетени като платени поне {(order.DepositPercent ?? 50m).ToString("0.##", culture)}% от общата сума — {Money(deposit)}. Отчетени като платени: {Money(paid)}. Остават за капаро: {Money(deposit - paid)}.";
            label = "Причина за потвърждаване без достатъчно капаро";
            message += canOverride
                ? " За потвърждаване по изключение въведете причина (до 1000 знака)."
                : " Нямате право за изключение. Отчетете необходимото плащане или се обърнете към служител с право за изключение.";
        }
        else if (target == OrderStatus.Delivered)
        {
            if (deferredDays > 0 || paid >= order.TotalAmount) return null;
            message = $"Не е отчетено плащане на остатъка от {Money(order.TotalAmount - paid)}. За тази поръчка няма отложено плащане.";
            label = "Причина за предаване без пълно доплащане";
            message += canOverride
                ? " Отчетете доплащането или въведете причина за предаване на поръчката без пълно доплащане (до 1000 знака)."
                : " Нямате право за изключение. Отчетете доплащането или се обърнете към служител с право за изключение.";
        }
        else return null;
        return new(message, label);
    }
}

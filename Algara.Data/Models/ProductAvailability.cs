namespace Algara.Data.Models;

public enum ProductAvailability
{
    Unknown,
    InStock,
    MadeToOrder,
    Inquiry
}

public static class DeliveryPolicy
{
    public const int DeliveryWorkingDays = 5;
    public const int ManufacturingWorkingDays = 30;
    public const int MaxStockQuantity = 1_000_000;

    public static ProductAvailability GetAvailability(int? stockQuantity, bool isUpholstered)
        => stockQuantity switch
        {
            null => ProductAvailability.Unknown,
            > 0 => ProductAvailability.InStock,
            0 when isUpholstered => ProductAvailability.MadeToOrder,
            0 => ProductAvailability.Inquiry,
            _ => throw new ArgumentOutOfRangeException(nameof(stockQuantity))
        };
}

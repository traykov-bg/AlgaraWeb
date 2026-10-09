namespace Algara.Data.Repositories;

public sealed class ProductSkuConflictException(string sku, Exception? innerException = null)
    : Exception("A product with this SKU already exists.", innerException)
{
    public string Sku { get; } = sku;
}

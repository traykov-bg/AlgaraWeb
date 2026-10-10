namespace Algara.Data.Models;

public class ProductVariant
{
    public int N { get; set; }
    public int ProductN { get; set; }
    public Product Product { get; set; } = null!;
    public int CatalogColorN { get; set; }
    public CatalogColor Color { get; set; } = null!;
    public string Sku { get; set; } = string.Empty;
    public int? StockQuantity { get; set; }
    public decimal Surcharge { get; set; }
    public int? ProductImageN { get; set; }
    public ProductImage? Image { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

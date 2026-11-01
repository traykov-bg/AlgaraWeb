namespace Algara.Data.Models;

/// <summary>One packaged parcel for one unit of a product, shared by its variants.</summary>
public class ProductShippingParcel
{
    public int N { get; set; }
    public int ProductN { get; set; }
    public Product Product { get; set; } = null!;
    public int Position { get; set; }
    public decimal WeightKg { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? DepthCm { get; set; }
}

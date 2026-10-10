namespace Algara.Data.Models;

public class ProductFabricSurcharge
{
    public int ProductN { get; set; }
    public int FabricN { get; set; }
    public decimal Surcharge { get; set; }
    public Product Product { get; set; } = null!;
    public Fabric Fabric { get; set; } = null!;
}

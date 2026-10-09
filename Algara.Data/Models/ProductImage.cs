namespace Algara.Data.Models;

public class ProductImage
{
    public int N { get; set; }
    public int ProductN { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public Product Product { get; set; } = null!;
}

namespace Algara.Data.Models;

public class ProductTranslation
{
    public int ProductN { get; set; }
    public string Language { get; set; } = "en";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Materials { get; set; }
    public Product Product { get; set; } = null!;
}

namespace Algara.Data.Models;

public class CatalogColor
{
    public int N { get; set; }
    public int? FabricN { get; set; }
    public Fabric? Fabric { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? EnglishName { get; set; }
    public string? HexColor { get; set; }
    public string? ColorGroup { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public string GetName(string? language = null)
        => CatalogTranslation.Value(Name, EnglishName, language)!;
}

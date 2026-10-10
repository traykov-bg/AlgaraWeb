namespace Algara.Data.Models;

public class Fabric
{
    public int N { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? EnglishName { get; set; }
    public string? Description { get; set; }
    public string? EnglishDescription { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<CatalogColor> Colors { get; set; } = new List<CatalogColor>();

    public string GetName(string? language = null)
        => CatalogTranslation.Value(Name, EnglishName, language)!;

    public string? GetDescription(string? language = null)
        => CatalogTranslation.Value(Description, EnglishDescription, language);
}

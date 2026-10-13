namespace Algara.Data.Models;

public class PartnerGroup
{
    public int N { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public int? ParentN { get; set; }
    public PartnerGroup? Parent { get; set; }
    public int DisplayOrder { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

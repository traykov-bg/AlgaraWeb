namespace Algara.Identity.Data
{
    public class ApplicationRole
    {
        public int N { get; set; }
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}

namespace Algara.Identity.Models;

public sealed class CourierConfiguration
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public bool UseSandbox { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? ProtectedPassword { get; set; }
    public string SenderClientNumber { get; set; } = string.Empty;
    public string SenderOfficeCode { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderPhone { get; set; } = string.Empty;
    public int? ServiceId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

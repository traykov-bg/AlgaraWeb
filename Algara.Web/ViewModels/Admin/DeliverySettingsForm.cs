using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Algara.Web.ViewModels.Admin;

public sealed class DeliverySettingsForm
{
    [Required] public CourierSettingsForm Econt { get; set; } = new();
    [Required] public CourierSettingsForm Speedy { get; set; } = new();
    [Required] public TransportSettingsForm Transport { get; set; } = new();
    [BindNever, ValidateNever] public IReadOnlyList<string> History { get; set; } = [];
}

public sealed class CourierSettingsForm
{
    [Required, StringLength(24)] public string ExpectedVersion { get; set; } = "none";
    public bool Enabled { get; set; }
    public bool UseSandbox { get; set; }
    [StringLength(200)] public string? UserName { get; set; }
    [StringLength(1000), DataType(DataType.Password)] public string? Password { get; set; }
    public bool RemovePassword { get; set; }
    [StringLength(64)] public string? SenderClientNumber { get; set; }
    [StringLength(64)] public string? SenderOfficeCode { get; set; }
    [StringLength(200)] public string? SenderName { get; set; }
    [StringLength(40)] public string? SenderPhone { get; set; }
    [Range(1, int.MaxValue)] public int? ServiceId { get; set; }
    [BindNever, ValidateNever] public bool HasReadablePassword { get; set; }
}

public sealed class TransportSettingsForm
{
    [Required, StringLength(24)] public string ExpectedVersion { get; set; } = "none";
    [Range(typeof(decimal), "-90", "90", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? OriginLatitude { get; set; }
    [Range(typeof(decimal), "-180", "180", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? OriginLongitude { get; set; }
    public bool GeocodingEnabled { get; set; }
    [StringLength(1024), DataType(DataType.Password)] public string? GeocodingKey { get; set; }
    public bool RemoveGeocodingKey { get; set; }
    [BindNever, ValidateNever] public bool HasReadableKey { get; set; }
}

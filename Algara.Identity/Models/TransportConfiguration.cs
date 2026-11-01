namespace Algara.Identity.Models;

public sealed class TransportConfiguration
{
    public int Id { get; set; } = 1;
    public decimal? OriginLatitude { get; set; }
    public decimal? OriginLongitude { get; set; }
    public bool GeocodingEnabled { get; set; }
    public string? ProtectedGeocodingKey { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

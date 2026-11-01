namespace Algara.Web.Services.Shipping.Carriers;

public enum CourierProvider { Econt = 1, Speedy = 2 }

// A short-lived, server-only connection. Never render or log this object.
public sealed class CourierConnection
{
    public CourierProvider Provider { get; init; }
    public bool UseSandbox { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string SenderClientNumber { get; init; } = string.Empty;
    public string SenderOfficeCode { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public string SenderPhone { get; init; } = string.Empty;
    public int? ServiceId { get; init; }
    public override string ToString() => $"Courier connection: {Provider}";
}

public sealed record CourierDestination(string City, string PostalCode, string? OfficeCode = null, string? Address = null);
public sealed record CourierParcel(decimal WeightKg, decimal WidthCm, decimal HeightCm, decimal DepthCm);
public sealed record CourierQuoteRequest(CourierDestination Destination, IReadOnlyList<CourierParcel> Parcels,
    decimal GoodsAmount, bool SenderPays);
public sealed record CourierOffice(string Code, string Name, string City, string Address);

public enum CourierErrorCode
{
    NotConfigured, InvalidDestination, InvalidParcels, Unavailable, Rejected, InvalidResponse, UnsupportedCurrency,
    UnsupportedParcels
}

public sealed record CourierOfficeResult(bool Success, IReadOnlyList<CourierOffice> Offices, CourierErrorCode? ErrorCode = null)
{
    public static CourierOfficeResult Failure(CourierErrorCode error) => new(false, [], error);
}

public sealed record CourierQuoteResult(bool Success, decimal? AmountEur, CourierErrorCode? ErrorCode = null,
    CourierOffice? Office = null)
{
    public static CourierQuoteResult Failure(CourierErrorCode error) => new(false, null, error);
}

public interface ICourierGateway
{
    Task<CourierOfficeResult> GetOfficeCitiesAsync(CourierConnection connection, CancellationToken cancellationToken = default) => Task.FromResult(CourierOfficeResult.Failure(CourierErrorCode.NotConfigured));
    Task<CourierOfficeResult> GetOfficesAsync(CourierConnection connection, string city, CancellationToken cancellationToken = default);
    Task<CourierQuoteResult> CalculateAsync(CourierConnection connection, CourierQuoteRequest request, CancellationToken cancellationToken = default);
}

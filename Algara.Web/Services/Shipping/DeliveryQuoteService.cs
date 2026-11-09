using Algara.Data.Models;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Shipping.Carriers;

namespace Algara.Web.Services.Shipping;

public enum DeliveryMethod { Pickup = 0, OwnTransport = 1, EcontOffice = 2, EcontAddress = 3, SpeedyOffice = 4, SpeedyAddress = 5 }
public sealed record DeliveryQuote(decimal GoodsAmount, decimal DeliveryAmount, DateTimeOffset CalculatedAt,
    double? DistanceKm = null, string? ResolvedAddress = null, bool IsTest = false, CourierOffice? Office = null)
{
    public decimal Total => GoodsAmount + DeliveryAmount;
}
public sealed record DeliveryQuoteResult(DeliveryQuote? Quote = null, string? ErrorKey = null);

public sealed class DeliveryQuoteService(IDeliverySettingsReader settings, ICourierGateway couriers,
    IAddressGeocoder geocoder, TimeProvider clock)
{
    public static bool IsOffice(DeliveryMethod method) => method is DeliveryMethod.EcontOffice or DeliveryMethod.SpeedyOffice;
    public static CourierProvider? Provider(DeliveryMethod method) => method switch
    {
        DeliveryMethod.EcontOffice or DeliveryMethod.EcontAddress => CourierProvider.Econt,
        DeliveryMethod.SpeedyOffice or DeliveryMethod.SpeedyAddress => CourierProvider.Speedy,
        _ => null
    };
    public static bool AllowsCourier(CartSnapshot cart) => cart.CanQuote && cart.Lines.All(x => x.Product is { AllowsCourier: true });
    public static bool AllowsOwnTransport(CartSnapshot cart) => cart.CanQuote && cart.Lines.Any(x => x.Product is { AllowsCourier: false });
    public static bool TryBuildParcels(CartSnapshot cart, out IReadOnlyList<CourierParcel> parcels)
    {
        var result = new List<CourierParcel>();
        parcels = result;
        if (!AllowsCourier(cart)) return false;
        foreach (var line in cart.Lines)
        {
            if (!ProductShippingRules.TryGetParcels(line.Product!, out var packages) ||
                result.Count + packages.Count * line.Entry.Quantity > CourierGateway.MaxQuoteParcels) return false;
            for (var unit = 0; unit < line.Entry.Quantity; unit++)
                result.AddRange(packages.Select(p => new CourierParcel(p.WeightKg, p.WidthCm, p.HeightCm, p.DepthCm)));
        }
        return result.Count > 0;
    }

    public async Task<DeliveryQuoteResult> CalculateAsync(CartSnapshot cart, DeliveryMethod method, string? city,
        string? postalCode, string? address, string? officeCode, CancellationToken ct = default)
    {
        if (!cart.CanQuote) return new(ErrorKey: "InvalidCart");
        if (!Enum.IsDefined(method)) return new(ErrorKey: "InvalidMethod");
        if (method == DeliveryMethod.Pickup) return Success(cart, 0m);
        if (method == DeliveryMethod.OwnTransport && !AllowsOwnTransport(cart)) return new(ErrorKey: "OwnTransportNotAllowed");
        city = city?.Trim(); postalCode = postalCode?.Trim(); address = address?.Trim(); officeCode = officeCode?.Trim();
        if (string.IsNullOrWhiteSpace(city) || city.Length is < 2 or > 100 || city.Any(char.IsControl))
            return new(ErrorKey: "InvalidAddress");
        if (IsOffice(method))
        {
            if (string.IsNullOrWhiteSpace(officeCode) || officeCode.Length > 40) return new(ErrorKey: "ChooseOffice");
        }
        else if (method != DeliveryMethod.OwnTransport && ((!string.IsNullOrWhiteSpace(postalCode) && !GoogleAddressGeocoder.ValidPostalCode(postalCode)) || string.IsNullOrWhiteSpace(address) ||
            address.Length > 500 || address.Any(char.IsControl))) return new(ErrorKey: "InvalidAddress");
        if (method == DeliveryMethod.OwnTransport)
        {
            var transport = await settings.GetTransportAsync(ct);
            if (transport == null) return new(ErrorKey: "TransportUnavailable");
            var resolved = await geocoder.ResolveAsync(transport.ApiKey, city, postalCode ?? string.Empty, address ?? string.Empty, ct);
            var destination = resolved.Address;
            if (destination == null) return new(ErrorKey: resolved.Error == GeocodingError.ServiceUnavailable ? "GeocodingUnavailable" : "AddressUnresolved");
            var distance = DistanceKm(transport.Latitude, transport.Longitude, destination.Latitude, destination.Longitude);
            return new(new(cart.GoodsSubtotal, OwnTransportAmount(distance), clock.GetUtcNow(), distance, destination.DisplayAddress));
        }
        if (!AllowsCourier(cart)) return new(ErrorKey: "CourierNotAllowed");
        if (!TryBuildParcels(cart, out var parcels)) return new(ErrorKey: "IncompleteParcels");
        var provider = Provider(method)!.Value;
        var connection = await settings.GetCourierAsync(provider, ct);
        if (connection == null) return new(ErrorKey: "CourierUnavailable");
        var free = cart.GoodsSubtotal > 100m;
        var request = new CourierQuoteRequest(new(city, postalCode ?? string.Empty,
            IsOffice(method) ? officeCode : null, IsOffice(method) ? null : address), parcels, cart.GoodsSubtotal, free);
        var result = await couriers.CalculateAsync(connection, request, ct);
        if (!result.Success || result.AmountEur is null or < 0) return new(ErrorKey: result.ErrorCode switch
        {
            CourierErrorCode.InvalidDestination => "InvalidDestination",
            CourierErrorCode.UnsupportedParcels => "UnsupportedParcels",
            _ => "CourierFailed"
        });
        return Success(cart, free ? 0m : result.AmountEur.Value, connection.UseSandbox, result.Office);
    }

    public async Task<CourierOfficeResult> CitiesAsync(CartSnapshot cart, DeliveryMethod method, CancellationToken ct)
    {
        if (!IsOffice(method) || !AllowsCourier(cart)) return CourierOfficeResult.Failure(CourierErrorCode.InvalidDestination);
        var connection = await settings.GetCourierAsync(Provider(method)!.Value, ct);
        return connection == null ? CourierOfficeResult.Failure(CourierErrorCode.NotConfigured) : await couriers.GetOfficeCitiesAsync(connection, ct);
    }

    public async Task<CourierOfficeResult> OfficesAsync(CartSnapshot cart, DeliveryMethod method, string? city, CancellationToken ct = default)
    {
        city = city?.Trim();
        if (!IsOffice(method) || !AllowsCourier(cart) || string.IsNullOrWhiteSpace(city) || city.Length is < 2 or > 100 || city.Any(char.IsControl))
            return CourierOfficeResult.Failure(CourierErrorCode.InvalidDestination);
        var connection = await settings.GetCourierAsync(Provider(method)!.Value, ct);
        return connection == null ? CourierOfficeResult.Failure(CourierErrorCode.NotConfigured) :
            await couriers.GetOfficesAsync(connection, city, ct);
    }

    private DeliveryQuoteResult Success(CartSnapshot cart, decimal amount, bool isTest = false, CourierOffice? office = null) => new(new(cart.GoodsSubtotal, amount, clock.GetUtcNow(), IsTest: isTest, Office: office));
    public static decimal OwnTransportAmount(double distanceKm) => !double.IsFinite(distanceKm) || distanceKm < 0
        ? throw new ArgumentOutOfRangeException(nameof(distanceKm)) : distanceKm <= 50 ? 0m : 100m;
    public static double DistanceKm(decimal latitude, decimal longitude, decimal destinationLatitude, decimal destinationLongitude)
    {
        if (latitude is < -90 or > 90 || destinationLatitude is < -90 or > 90 || longitude is < -180 or > 180 || destinationLongitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(latitude));
        const double radians = Math.PI / 180;
        var lat1 = (double)latitude * radians;
        var lat2 = (double)destinationLatitude * radians;
        var dlat = lat2 - lat1;
        var dlon = (double)(destinationLongitude - longitude) * radians;
        var a = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dlon / 2), 2);
        return 6371.0088 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}

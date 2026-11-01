using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Algara.Web.Services.Shipping.Carriers;

public sealed partial class CourierGateway
{
    private static readonly Uri SpeedyOfficesEndpoint = new("https://api.speedy.bg/v1/location/office");
    private static readonly Uri SpeedyCalculateEndpoint = new("https://api.speedy.bg/v1/calculate");

    private static JsonObject SpeedyCredentials(CourierConnection connection) => new()
    {
        ["userName"] = connection.UserName,
        ["password"] = connection.Password,
        ["language"] = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "EN" : "BG"
    };

    private async Task<CourierOfficeResult> GetSpeedyOfficesAsync(CourierConnection connection, string city,
        CancellationToken cancellationToken, bool allCities = false)
    {
        var payload = SpeedyCredentials(connection);
        payload["countryId"] = 100;
        if (!allCities) payload["siteName"] = city;
        payload["officeType"] = "OFFICE";
        payload["officeFeatures"] = new JsonArray("CASH_PAYMENT", "PICK_UP", "CARGO_TYPE_PARCEL");
        if (!allCities) payload["limit"] = MaxOfficeResults;
        using var document = await PostAsync(SpeedyOfficesEndpoint, payload, connection, cancellationToken);
        if (HasError(document.RootElement)) return CourierOfficeResult.Failure(CourierErrorCode.Rejected);
        var rows = Property(document.RootElement, "offices");
        if (rows.ValueKind != JsonValueKind.Array) return CourierOfficeResult.Failure(CourierErrorCode.InvalidResponse);
        var offices = new List<CourierOffice>();
        foreach (var row in rows.EnumerateArray())
        {
            if (Text(Property(row, "type")) != "OFFICE") continue;
            if (Property(row, "pickUpAllowed").ValueKind == JsonValueKind.False ||
                Property(row, "cashPaymentAllowed").ValueKind == JsonValueKind.False) continue;
            var code = Text(Property(row, "id"));
            var name = Text(Property(row, "name"));
            var address = Property(row, "address");
            var localCity = Text(Property(address, "siteName"));
            var fullAddress = Text(Property(address, "fullAddressString"));
            var country = Text(Property(address, "countryId"));
            if (country.Length > 0 && country != "100") continue;
            if (!PositiveInt(code) || !IsText(name, 300) || !IsText(localCity, 100) || fullAddress.Length > 1000) continue;
            offices.Add(new(code, name, localCity, fullAddress));
        }
        return new(true, offices.DistinctBy(o => o.Code).Take(allCities ? int.MaxValue : MaxOfficeResults).ToArray());
    }

    private async Task<CourierQuoteResult> CalculateSpeedyAsync(CourierConnection connection, CourierQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Destination.OfficeCode) && !PositiveInt(request.Destination.OfficeCode))
            return CourierQuoteResult.Failure(CourierErrorCode.InvalidDestination);
        var payload = SpeedyCredentials(connection);
        var sender = new JsonObject { ["clientId"] = long.Parse(connection.SenderClientNumber, CultureInfo.InvariantCulture) };
        if (!string.IsNullOrEmpty(connection.SenderOfficeCode))
            sender["dropoffOfficeId"] = int.Parse(connection.SenderOfficeCode, CultureInfo.InvariantCulture);
        payload["sender"] = sender;
        var recipient = new JsonObject { ["privatePerson"] = true };
        if (!string.IsNullOrEmpty(request.Destination.OfficeCode))
            recipient["pickupOfficeId"] = int.Parse(request.Destination.OfficeCode, CultureInfo.InvariantCulture);
        else
            recipient["addressLocation"] = new JsonObject
            {
                ["countryId"] = 100,
                ["siteName"] = request.Destination.City.Trim(),
                ["postCode"] = request.Destination.PostalCode.Trim()
            };
        payload["recipient"] = recipient;
        payload["service"] = new JsonObject
        {
            ["serviceIds"] = new JsonArray(JsonValue.Create(connection.ServiceId!.Value)),
            ["autoAdjustPickupDate"] = true,
            ["additionalServices"] = new JsonObject
            {
                ["cod"] = new JsonObject
                {
                    ["amount"] = request.GoodsAmount,
                    ["currencyCode"] = "EUR",
                    ["processingType"] = "CASH",
                    ["includeShippingPrice"] = false
                }
            }
        };
        payload["content"] = new JsonObject
        {
            ["parcelsCount"] = request.Parcels.Count,
            ["totalWeight"] = request.Parcels.Sum(p => p.WeightKg),
            ["parcels"] = new JsonArray(request.Parcels.Select((p, index) => (JsonNode)new JsonObject
            {
                ["seqNo"] = index + 1,
                ["weight"] = p.WeightKg,
                // Speedy requires integer centimetres; round up, never understate volume.
                ["size"] = new JsonObject
                {
                    ["width"] = (int)decimal.Ceiling(p.WidthCm),
                    ["height"] = (int)decimal.Ceiling(p.HeightCm),
                    ["depth"] = (int)decimal.Ceiling(p.DepthCm)
                }
            }).ToArray())
        };
        payload["payment"] = new JsonObject { ["courierServicePayer"] = request.SenderPays ? "SENDER" : "RECIPIENT" };
        using var document = await PostAsync(SpeedyCalculateEndpoint, payload, connection, cancellationToken);
        if (HasError(document.RootElement)) return CourierQuoteResult.Failure(CourierErrorCode.Rejected);
        var rows = Property(document.RootElement, "calculations");
        if (rows.ValueKind != JsonValueKind.Array) return CourierQuoteResult.Failure(CourierErrorCode.InvalidResponse);
        var matching = rows.EnumerateArray().Where(row =>
            Text(Property(row, "serviceId")) == connection.ServiceId.Value.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (matching.Length != 1) return CourierQuoteResult.Failure(CourierErrorCode.InvalidResponse);
        if (HasError(matching[0])) return CourierQuoteResult.Failure(CourierErrorCode.Rejected);
        var price = Property(matching[0], "price");
        if (price.ValueKind != JsonValueKind.Object) return CourierQuoteResult.Failure(CourierErrorCode.InvalidResponse);
        return ParsePrice(Property(price, "total"), Property(price, "currency"));
    }
}

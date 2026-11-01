using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Algara.Web.Services.Shipping.Carriers;

public sealed partial class CourierGateway
{
    // Official JSON endpoints; no configurable host or shipment-creation mode.
    private static Uri EcontEndpoint(CourierConnection connection, bool offices) => new(
        (connection.UseSandbox ? "https://demo.econt.com/ee/services/" : "https://ee.econt.com/services/") +
        (offices ? "Nomenclatures/NomenclaturesService.getOffices.json" : "Shipments/LabelService.createLabel.json"));

    private async Task<CourierOfficeResult> GetEcontOfficesAsync(CourierConnection connection, string city,
        CancellationToken cancellationToken, bool allCities = false)
    {
        using var document = await PostAsync(EcontEndpoint(connection, true),
            new JsonObject { ["countryCode"] = "BGR" }, connection, cancellationToken);
        if (HasError(document.RootElement)) return CourierOfficeResult.Failure(CourierErrorCode.Rejected);
        var rows = Property(document.RootElement, "offices");
        if (rows.ValueKind != JsonValueKind.Array) return CourierOfficeResult.Failure(CourierErrorCode.InvalidResponse);
        var offices = new List<CourierOffice>();
        foreach (var row in rows.EnumerateArray())
        {
            if (IsTrue(Property(row, "isAPS"))) continue;
            var address = Property(row, "address");
            var location = Property(address, "city");
            var localCity = Text(Property(location, "name"));
            var englishCity = Text(Property(location, "nameEn"));
            if (!allCities && !localCity.Contains(city, StringComparison.OrdinalIgnoreCase) &&
                !englishCity.Contains(city, StringComparison.OrdinalIgnoreCase)) continue;
            var country = Text(Property(Property(location, "country"), "code3"));
            if (country.Length > 0 && country != "BGR") continue;
            var types = Property(row, "shipmentTypes");
            // Office nomenclatures advertise ordinary parcels as "courier";
            // "pack" is the label shipment type (also used by the generic schema).
            if (types.ValueKind == JsonValueKind.Array &&
                !types.EnumerateArray().Any(t => Text(t).Equals("courier", StringComparison.OrdinalIgnoreCase) ||
                    Text(t).Equals("pack", StringComparison.OrdinalIgnoreCase))) continue;
            var code = Text(Property(row, "code"));
            var english = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
            var name = Text(Property(row, english ? "nameEn" : "name"));
            if (name.Length == 0) name = Text(Property(row, "name"));
            var fullAddress = Text(Property(address, english ? "fullAddressEn" : "fullAddress"));
            if (fullAddress.Length == 0) fullAddress = Text(Property(address, "fullAddress"));
            if (!IsCode(code) || !IsText(name, 300) || !IsText(localCity, 100) || fullAddress.Length > 1000) continue;
            offices.Add(new(code, name, localCity, fullAddress));
        }
        return new(true, offices.DistinctBy(o => o.Code).OrderBy(o => o.Name)
            .Take(allCities ? 10000 : MaxOfficeResults).ToArray());
    }

    private async Task<CourierQuoteResult> CalculateEcontAsync(CourierConnection connection, CourierQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var label = new JsonObject
        {
            ["senderClient"] = new JsonObject
            {
                ["clientNumber"] = connection.SenderClientNumber,
                ["name"] = connection.SenderName,
                ["phones"] = new JsonArray(JsonValue.Create(connection.SenderPhone))
            },
            ["senderOfficeCode"] = connection.SenderOfficeCode,
            ["shipmentType"] = "pack",
            ["shipmentDescription"] = "Home goods",
            ["packCount"] = request.Parcels.Count,
            ["weight"] = request.Parcels.Sum(p => p.WeightKg),
            ["shipmentDimensionsW"] = request.Parcels[0].WidthCm,
            ["shipmentDimensionsH"] = request.Parcels[0].HeightCm,
            ["shipmentDimensionsL"] = request.Parcels[0].DepthCm,
            ["paymentSenderMethod"] = "cash",
            ["paymentReceiverMethod"] = "cash",
            ["paymentReceiverAmountIsPercent"] = true,
            ["paymentReceiverAmount"] = request.SenderPays ? 0 : 100,
            ["services"] = new JsonObject
            {
                ["cdAmount"] = request.GoodsAmount,
                ["cdType"] = "get",
                ["cdCurrency"] = "EUR"
            }
        };
        if (!string.IsNullOrEmpty(request.Destination.OfficeCode))
            label["receiverOfficeCode"] = request.Destination.OfficeCode;
        else
            label["receiverAddress"] = new JsonObject
            {
                ["city"] = new JsonObject
                {
                    ["country"] = new JsonObject { ["code3"] = "BGR" },
                    ["name"] = request.Destination.City.Trim(),
                    ["postCode"] = request.Destination.PostalCode.Trim()
                },
                ["fullAddress"] = request.Destination.Address!.Trim()
            };

        using var document = await PostAsync(EcontEndpoint(connection, false),
            new JsonObject { ["label"] = label, ["mode"] = "calculate" }, connection, cancellationToken);
        if (HasError(document.RootElement)) return CourierQuoteResult.Failure(CourierErrorCode.Rejected);
        var result = Property(document.RootElement, "label");
        if (result.ValueKind != JsonValueKind.Object) return CourierQuoteResult.Failure(CourierErrorCode.InvalidResponse);
        return ParsePrice(Property(result, "totalPrice"), Property(result, "currency"));
    }
}

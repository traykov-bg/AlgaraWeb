using System.Globalization;
using System.Text.Json;

namespace Algara.Web.Services.Shipping;

public sealed record GeocodedAddress(decimal Latitude, decimal Longitude, string DisplayAddress);
public enum GeocodingError { AddressUnresolved, ServiceUnavailable }
public sealed record GeocodingResult(GeocodedAddress? Address = null, GeocodingError? Error = null);
public interface IAddressGeocoder
{
    Task<GeocodingResult> ResolveAsync(string apiKey, string city, string postalCode, string address, CancellationToken ct = default);
}

public sealed class GoogleAddressGeocoder(HttpClient client) : IAddressGeocoder
{
    public async Task<GeocodedAddress?> FindAsync(string apiKey, string city, string postalCode, string address, CancellationToken ct = default)
        => (await ResolveAsync(apiKey, city, postalCode, address, ct)).Address;

    public async Task<GeocodingResult> ResolveAsync(string apiKey, string city, string postalCode, string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(city) ||
            city.Length > 100 ||
            city.Contains('|') || city.Contains(':') || city.Any(char.IsControl)) return new(Error: GeocodingError.ServiceUnavailable);
        var uri = "https://maps.googleapis.com/maps/api/geocode/json?address=" + Uri.EscapeDataString(city.Trim()) +
            "&components=" + Uri.EscapeDataString("country:BG") +
            "&language=bg&key=" + Uri.EscapeDataString(apiKey);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            // Resolve the settlement only. Postal code and delivery instructions never enter the Google request.
            // The named HttpClient has all logging removed: the query includes a private API key.
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 256 * 1024) return new(Error: GeocodingError.ServiceUnavailable);
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int size;
            while ((size = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + size > 256 * 1024) return new(Error: GeocodingError.ServiceUnavailable);
                output.Write(buffer, 0, size);
            }
            using var json = JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
            var root = json.RootElement;
            if (Text(root, "status") == "ZERO_RESULTS") return new(Error: GeocodingError.AddressUnresolved);
            if (Text(root, "status") != "OK") return new(Error: GeocodingError.ServiceUnavailable);
            var found = Parse(root);
            return found == null ? new(Error: GeocodingError.AddressUnresolved) : new(Address: found);
        }
        catch (HttpRequestException) { return new(Error: GeocodingError.ServiceUnavailable); }
        catch (IOException) { return new(Error: GeocodingError.ServiceUnavailable); }
        catch (JsonException) { return new(Error: GeocodingError.ServiceUnavailable); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(Error: GeocodingError.ServiceUnavailable); }
    }

    public static GeocodedAddress? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || Text(root, "status") != "OK" || !root.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 1) return null;
        var result = results[0];
        if (result.ValueKind != JsonValueKind.Object ||
            (result.TryGetProperty("partial_match", out var partial) && partial.ValueKind != JsonValueKind.False) ||
            !result.TryGetProperty("address_components", out var components) || components.ValueKind != JsonValueKind.Array ||
            !result.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object ||
            !geometry.TryGetProperty("location", out var location) ||
            location.ValueKind != JsonValueKind.Object || !location.TryGetProperty("lat", out var lat) ||
            !location.TryGetProperty("lng", out var lon) || lat.ValueKind != JsonValueKind.Number || lon.ValueKind != JsonValueKind.Number ||
            !lat.TryGetDecimal(out var latitude) || !lon.TryGetDecimal(out var longitude) ||
            latitude is < 41 or > 44.5m || longitude is < 22 or > 29) return null;
        var parts = components.EnumerateArray().ToArray();
        bool Has(string type, string? value = null) => parts.Any(p =>
            p.ValueKind == JsonValueKind.Object && p.TryGetProperty("types", out var types) && types.ValueKind == JsonValueKind.Array &&
            types.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == type) &&
            (value == null || Text(p, "short_name") == value));
        var formatted = Text(result, "formatted_address");
        return Has("country", "BG") && Has("locality") &&
            result.TryGetProperty("types", out var resultTypes) && resultTypes.ValueKind == JsonValueKind.Array &&
            resultTypes.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "locality") &&
            !string.IsNullOrWhiteSpace(formatted) && formatted.Length <= 1000
            ? new(latitude, longitude, formatted) : null;
    }

    public static bool ValidPostalCode(string? value) => value is { Length: 4 } && value.All(char.IsAsciiDigit);
    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.String ? child.GetString() : null;
}

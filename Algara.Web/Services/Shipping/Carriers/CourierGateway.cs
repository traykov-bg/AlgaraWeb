using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Algara.Web.Services.Shipping.Carriers;

/// <summary>
/// Read-only courier integration: office nomenclatures and calculation only.
/// Register its HttpClient with CreateHandler so redirects never forward credentials.
/// </summary>
public sealed partial class CourierGateway(HttpClient client) : ICourierGateway
{
    public const int MaxQuoteParcels = 500;
    public const int MaxResponseBytes = 4 * 1024 * 1024;
    public const int MaxOfficeResults = 200;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    public static bool IsConfigured(CourierConnection connection, bool requireSender = true)
    {
        if (!Enum.IsDefined(connection.Provider) || !IsText(connection.UserName, 200) ||
            !IsText(connection.Password, 1000) || connection.UserName.Contains(':')) return false;
        if (connection.Provider == CourierProvider.Speedy && connection.UseSandbox) return false;
        if (!requireSender) return true;
        return connection.Provider switch
        {
            CourierProvider.Econt => IsText(connection.SenderClientNumber, 100) &&
                IsCode(connection.SenderOfficeCode) && IsText(connection.SenderName, 200) &&
                IsText(connection.SenderPhone, 40),
            CourierProvider.Speedy => PositiveLong(connection.SenderClientNumber) &&
                (string.IsNullOrEmpty(connection.SenderOfficeCode) || PositiveInt(connection.SenderOfficeCode)) &&
                connection.ServiceId is > 0,
            _ => false
        };
    }

    public async Task<CourierOfficeResult> GetOfficeCitiesAsync(CourierConnection connection, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(connection, false))
            return CourierOfficeResult.Failure(CourierErrorCode.NotConfigured);
        try { return connection.Provider == CourierProvider.Econt
            ? await GetEcontOfficesAsync(connection, string.Empty, cancellationToken, true)
            : await GetSpeedyOfficesAsync(connection, string.Empty, cancellationToken, true); }
        catch (CourierRequestException ex) { return CourierOfficeResult.Failure(ex.ErrorCode); }
        catch (JsonException) { return CourierOfficeResult.Failure(CourierErrorCode.InvalidResponse); }
        catch (HttpRequestException) { return CourierOfficeResult.Failure(CourierErrorCode.Unavailable); }
        catch (IOException) { return CourierOfficeResult.Failure(CourierErrorCode.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return CourierOfficeResult.Failure(CourierErrorCode.Unavailable); }
    }

    public async Task<CourierOfficeResult> GetOfficesAsync(CourierConnection connection, string city,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(connection, false)) return CourierOfficeResult.Failure(CourierErrorCode.NotConfigured);
        if (!IsText(city, 100) || city.Trim().Length < 2)
            return CourierOfficeResult.Failure(CourierErrorCode.InvalidDestination);
        try
        {
            return connection.Provider == CourierProvider.Econt
                ? await GetEcontOfficesAsync(connection, city.Trim(), cancellationToken)
                : await GetSpeedyOfficesAsync(connection, city.Trim(), cancellationToken);
        }
        catch (CourierRequestException ex) { return CourierOfficeResult.Failure(ex.ErrorCode); }
        catch (JsonException) { return CourierOfficeResult.Failure(CourierErrorCode.InvalidResponse); }
        catch (HttpRequestException) { return CourierOfficeResult.Failure(CourierErrorCode.Unavailable); }
        catch (IOException) { return CourierOfficeResult.Failure(CourierErrorCode.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return CourierOfficeResult.Failure(CourierErrorCode.Unavailable); }
    }

    public async Task<CourierQuoteResult> CalculateAsync(CourierConnection connection, CourierQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(connection)) return CourierQuoteResult.Failure(CourierErrorCode.NotConfigured);
        if (!ValidDestination(request.Destination)) return CourierQuoteResult.Failure(CourierErrorCode.InvalidDestination);
        if (request.Parcels is not { Count: > 0 and <= MaxQuoteParcels } ||
            request.Parcels.Any(p => p.WeightKg is <= 0 or > 100000 ||
                p.WidthCm is <= 0 or > 100000 || p.HeightCm is <= 0 or > 100000 || p.DepthCm is <= 0 or > 100000) ||
            request.GoodsAmount is <= 0 or > 1_000_000_000m)
            return CourierQuoteResult.Failure(CourierErrorCode.InvalidParcels);

        // Econt documents per-pack dimensions explicitly for pallets, but PACK
        // dimensions at shipment level. Do not fabricate a combined box or quote
        // multiple PACKs by weight alone; Speedy supports the complete parcel list.
        if (connection.Provider == CourierProvider.Econt && request.Parcels.Count != 1)
            return CourierQuoteResult.Failure(CourierErrorCode.UnsupportedParcels);

        // A posted identifier alone is not an office selection. Recheck current carrier
        // nomenclatures, including the OFFICE (not locker) restriction, before every quote.
        CourierOffice? selectedOffice = null;
        if (!string.IsNullOrEmpty(request.Destination.OfficeCode))
        {
            var offices = await GetOfficesAsync(connection, request.Destination.City, cancellationToken);
            if (!offices.Success) return CourierQuoteResult.Failure(offices.ErrorCode!.Value);
            selectedOffice = offices.Offices.FirstOrDefault(o => o.Code == request.Destination.OfficeCode);
            if (selectedOffice is null)
                return CourierQuoteResult.Failure(CourierErrorCode.InvalidDestination);
        }

        try
        {
            var result = connection.Provider == CourierProvider.Econt
                ? await CalculateEcontAsync(connection, request, cancellationToken)
                : await CalculateSpeedyAsync(connection, request, cancellationToken);
            return result.Success ? result with { Office = selectedOffice } : result;
        }
        catch (CourierRequestException ex) { return CourierQuoteResult.Failure(ex.ErrorCode); }
        catch (JsonException) { return CourierQuoteResult.Failure(CourierErrorCode.InvalidResponse); }
        catch (HttpRequestException) { return CourierQuoteResult.Failure(CourierErrorCode.Unavailable); }
        catch (IOException) { return CourierQuoteResult.Failure(CourierErrorCode.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return CourierQuoteResult.Failure(CourierErrorCode.Unavailable); }
    }

    private async Task<JsonDocument> PostAsync(Uri uri, JsonObject payload, CourierConnection connection,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (connection.Provider == CourierProvider.Econt)
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(connection.UserName + ":" + connection.Password)));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new CourierRequestException(response.StatusCode is System.Net.HttpStatusCode.BadRequest or
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? CourierErrorCode.Rejected : CourierErrorCode.Unavailable);
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new CourierRequestException(CourierErrorCode.InvalidResponse);
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (output.Length + count > MaxResponseBytes)
                throw new CourierRequestException(CourierErrorCode.InvalidResponse);
            output.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static bool ValidDestination(CourierDestination destination) =>
        IsText(destination.City, 100) && destination.City.Trim().Length >= 2 &&
        (string.IsNullOrEmpty(destination.OfficeCode)
            ? IsText(destination.PostalCode, 10) && IsText(destination.Address, 500)
            : IsCode(destination.OfficeCode));

    private static bool IsText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);
    private static bool IsCode(string? value) => IsText(value, 40) && value!.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    private static bool PositiveLong(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0;
    private static bool PositiveInt(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0;
    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var property) ? property : default;
    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => string.Empty
    };
    private static bool IsTrue(JsonElement value) => value.ValueKind == JsonValueKind.True;
    private static bool HasError(JsonElement value) => Property(value, "error").ValueKind is not
        (JsonValueKind.Undefined or JsonValueKind.Null) ||
        (Property(value, "type").ValueKind == JsonValueKind.String && Property(value, "message").ValueKind == JsonValueKind.String);

    private static CourierQuoteResult ParsePrice(JsonElement amount, JsonElement currency)
    {
        if (Text(currency) != "EUR") return CourierQuoteResult.Failure(CourierErrorCode.UnsupportedCurrency);
        if (!TryDecimal(amount, out var price) || price < 0 || price > 1_000_000m)
            return CourierQuoteResult.Failure(CourierErrorCode.InvalidResponse);
        return new(true, decimal.Round(price, 2, MidpointRounding.AwayFromZero));
    }

    private static bool TryDecimal(JsonElement value, out decimal result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number ? value.TryGetDecimal(out result) :
            value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
    }

    private sealed class CourierRequestException(CourierErrorCode errorCode) : Exception("Courier request failed.")
    {
        public CourierErrorCode ErrorCode { get; } = errorCode;
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Algara.Web.Services.Shipping;
using Microsoft.AspNetCore.WebUtilities;

namespace Algara.UnitTests.Services;

public sealed class GoogleAddressGeocoderTests
{
    private const string Secret = "synthetic-private-key+?&";

    [Fact]
    public async Task SettlementLookupUsesOnlyCityAndCountry()
    {
        using var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var uri = request.RequestUri!;
            Assert.Equal("https", uri.Scheme);
            Assert.Equal("maps.googleapis.com", uri.Host);
            Assert.Equal("/maps/api/geocode/json", uri.AbsolutePath);
            var query = QueryHelpers.ParseQuery(uri.Query);
            Assert.Equal("Plovdiv", query["address"]);
            Assert.Equal("country:BG", query["components"]);
            Assert.Equal("bg", query["language"]);
            Assert.Equal(Secret, query["key"]);
            Assert.Null(request.Content);
            return Task.FromResult(Json(Success()));
        });
        using var client = new HttpClient(handler);
        var result = await new GoogleAddressGeocoder(client).FindAsync(Secret, "Plovdiv", "4000", "Example street 1");
        Assert.NotNull(result);
        Assert.Equal(42.145m, result.Latitude);
        Assert.Equal(24.749m, result.Longitude);
        Assert.Equal("Plovdiv, Bulgaria", result.DisplayAddress);
        Assert.DoesNotContain(Secret, result.ToString());
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("partial-null")]
    [InlineData("ambiguous")]
    [InlineData("wrong-country")]
    [InlineData("missing-locality")]
    [InlineData("street-result")]
    [InlineData("missing-result-types")]
    [InlineData("latitude-outside")]
    [InlineData("longitude-outside")]
    [InlineData("string-latitude")]
    [InlineData("missing-geometry")]
    [InlineData("missing-components")]
    [InlineData("missing-formatted")]
    [InlineData("too-long-formatted")]
    [InlineData("not-ok")]
    [InlineData("zero-results")]
    public async Task UncertainOrMismatchedAddressNeverProducesADistanceInput(string scenario)
    {
        var payload = JsonNode.Parse(Success())!.AsObject();
        var result = payload["results"]![0]!;
        var components = result["address_components"]!.AsArray();
        switch (scenario)
        {
            case "partial": result["partial_match"] = true; break;
            case "partial-null": result["partial_match"] = null; break;
            case "centroid": result["geometry"]!["location_type"] = "GEOMETRIC_CENTER"; break;
            case "approximate": result["geometry"]!["location_type"] = "APPROXIMATE"; break;
            case "range-interpolated": result["geometry"]!["location_type"] = "RANGE_INTERPOLATED"; break;
            case "ambiguous": payload["results"]!.AsArray().Add(result.DeepClone()); break;
            case "wrong-country": components[0]!["short_name"] = "GR"; break;
            case "wrong-postcode": components[1]!["short_name"] = "1000"; break;
            case "missing-locality": components.RemoveAt(2); break;
            case "street-result": result["types"] = new JsonArray("street_address"); break;
            case "missing-result-types": result.AsObject().Remove("types"); break;
            case "latitude-outside": result["geometry"]!["location"]!["lat"] = 40.99m; break;
            case "longitude-outside": result["geometry"]!["location"]!["lng"] = 29.01m; break;
            case "string-latitude": result["geometry"]!["location"]!["lat"] = "42.145"; break;
            case "missing-geometry": result.AsObject().Remove("geometry"); break;
            case "missing-components": result.AsObject().Remove("address_components"); break;
            case "missing-formatted": result.AsObject().Remove("formatted_address"); break;
            case "too-long-formatted": result["formatted_address"] = new string('x', 1001); break;
            case "not-ok": payload["status"] = "REQUEST_DENIED"; break;
            case "zero-results": payload["results"] = new JsonArray(); break;
        }
        using var handler = new StubHandler((_, _) => Task.FromResult(Json(payload.ToJsonString())));
        using var client = new HttpClient(handler);
        Assert.Null(await new GoogleAddressGeocoder(client).FindAsync(Secret, "Plovdiv", "4000", "Example street 1"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"OK\",\"results\":{}}")]
    [InlineData("{\"status\":\"OK\",\"results\":[null]}")]
    public async Task MalformedOrWrongRootShapeReturnsUnavailable(string body)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Json(body)));
        using var client = new HttpClient(handler);
        Assert.Null(await new GoogleAddressGeocoder(client).FindAsync(Secret, "Plovdiv", "4000", "Example street 1"));
    }

    [Theory]
    [InlineData("", "Plovdiv", "4000", "Example street 1")]
    [InlineData("key", "Plovdiv|country:GR", "4000", "Example street 1")]
    [InlineData("key", "Plovdiv:GR", "4000", "Example street 1")]
    [InlineData("key", "", "4000", "Street 1")]
    [InlineData("key", "Plovdiv\n", "4000", "Street 1")]
    public async Task InvalidInputCannotReachProvider(string key, string city, string postalCode, string address)
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("HTTP must not be attempted."));
        using var client = new HttpClient(handler);
        Assert.Null(await new GoogleAddressGeocoder(client).FindAsync(key, city, postalCode, address));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FailedLaterLookupCannotReusePreviousSuccessfulCoordinates()
    {
        var calls = 0;
        using var handler = new StubHandler((_, _) => Task.FromResult(
            Json(++calls == 1 ? Success() : """{"status":"ZERO_RESULTS","results":[]}""")));
        using var client = new HttpClient(handler);
        var geocoder = new GoogleAddressGeocoder(client);
        Assert.NotNull(await geocoder.FindAsync(Secret, "Plovdiv", "4000", "Example street 1"));
        Assert.Null(await geocoder.FindAsync(Secret, "Sofia", "1000", "Another street 2"));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedKnownOrStreamingResponseIsRejected(bool contentLengthKnown)
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 256 * 1024 + 1));
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = contentLengthKnown ? new ByteArrayContent(bytes) : new UnknownLengthContent(bytes)
        }));
        using var client = new HttpClient(handler);
        Assert.Null(await new GoogleAddressGeocoder(client).FindAsync(Secret, "Plovdiv", "4000", "Example street 1"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpFailureNeverReturnsProviderBody(HttpStatusCode status)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        { Content = new StringContent(Secret) }));
        using var client = new HttpClient(handler);
        Assert.Null(await new GoogleAddressGeocoder(client).FindAsync(Secret, "Plovdiv", "4000", "Example street 1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkOrTimeoutExceptionsContainingApiKeyAreNotExposed(bool timeout)
    {
        using var handler = new StubHandler((request, _) => Task.FromException<HttpResponseMessage>(timeout
            ? new TaskCanceledException(request.RequestUri!.AbsoluteUri)
            : new HttpRequestException(request.RequestUri!.AbsoluteUri)));
        using var client = new HttpClient(handler);
        Assert.Null(await new GoogleAddressGeocoder(client).FindAsync(Secret, "Plovdiv", "4000", "Example street 1"));
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var handler = new StubHandler((_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GoogleAddressGeocoder(client)
            .FindAsync(Secret, "Plovdiv", "4000", "Example street 1", cancellation.Token));
    }

    [Fact]
    public void ParseAllowsExplicitFalsePartialMarkerButRejectsBrokenComponents()
    {
        var payload = JsonNode.Parse(Success())!;
        payload["results"]![0]!["partial_match"] = false;
        using var valid = JsonDocument.Parse(payload.ToJsonString());
        Assert.NotNull(GoogleAddressGeocoder.Parse(valid.RootElement));
        payload["results"]![0]!["address_components"] = new JsonArray((JsonNode?)null, JsonValue.Create(1), new JsonObject());
        using var malformed = JsonDocument.Parse(payload.ToJsonString());
        Assert.Null(GoogleAddressGeocoder.Parse(malformed.RootElement));
    }

    [Theory]
    [InlineData("")]
    [InlineData("8001")]
    [InlineData("8001|country:GR")]
    public async Task PostalCodeNeverFiltersOrRejectsAnOtherwiseExactAddress(string postalCode)
    {
        using var handler = new StubHandler((request, _) => {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("country:BG", query["components"]);
            return Task.FromResult(Json(Success()));
        });
        using var client = new HttpClient(handler);
        var result = await new GoogleAddressGeocoder(client).ResolveAsync(Secret, "Plovdiv", postalCode, "Example street 1");
        Assert.NotNull(result.Address);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("ZERO_RESULTS", GeocodingError.AddressUnresolved)]
    [InlineData("REQUEST_DENIED", GeocodingError.ServiceUnavailable)]
    [InlineData("OVER_QUERY_LIMIT", GeocodingError.ServiceUnavailable)]
    [InlineData("OVER_DAILY_LIMIT", GeocodingError.ServiceUnavailable)]
    [InlineData("UNKNOWN_ERROR", GeocodingError.ServiceUnavailable)]
    public async Task ProviderFailureIsDistinctFromAnUnresolvedAddressWithoutLeakingDetails(string status, GeocodingError error)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(new { status, error_message = Secret, results = Array.Empty<object>() }))));
        using var client = new HttpClient(handler);
        var result = await new GoogleAddressGeocoder(client).ResolveAsync(Secret, "Plovdiv", "", "Example street 1");
        Assert.Equal(error, result.Error);
        Assert.Null(result.Address);
        Assert.DoesNotContain(Secret, result.ToString());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("4000", "офис на Спиди, Пловдив, бул. Никола Вапцаров 9")]
    [InlineData("9999", "Unrecognized delivery instructions")]
    public async Task DeliveryInstructionsNeverAffectSettlementQuery(string postalCode, string address)
    {
        using var handler = new StubHandler((request, _) => {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("Plovdiv", query["address"]);
            Assert.Equal("country:BG", query["components"]);
            return Task.FromResult(Json(Success()));
        });
        using var client = new HttpClient(handler);
        var result = await new GoogleAddressGeocoder(client).ResolveAsync(Secret, "Plovdiv", postalCode, address);
        Assert.NotNull(result.Address);
    }

    [Theory]
    [InlineData("APPROXIMATE")]
    [InlineData("GEOMETRIC_CENTER")]
    public void SettlementCoordinatesDoNotRequireRooftopPrecision(string locationType)
    {
        var payload = JsonNode.Parse(Success())!;
        payload["results"]![0]!["geometry"]!["location_type"] = locationType;
        using var json = JsonDocument.Parse(payload.ToJsonString());
        Assert.NotNull(GoogleAddressGeocoder.Parse(json.RootElement));
    }

    private static string Success() => """
        {
          "status":"OK",
          "results":[{
            "formatted_address":"Plovdiv, Bulgaria",
            "types":["locality","political"],
            "address_components":[
              {"short_name":"BG","types":["country","political"]},
              {"short_name":"4000","types":["postal_code"]},
              {"short_name":"Plovdiv","types":["locality","political"]}
            ],
            "geometry":{"location_type":"APPROXIMATE","location":{"lat":42.145,"lng":24.749}}
          }]
        }
        """;
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return respond(request, cancellationToken);
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}

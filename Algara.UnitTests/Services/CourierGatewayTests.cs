using System.Net;
using System.Text;
using System.Text.Json;
using Algara.Web.Services.Shipping.Carriers;

namespace Algara.UnitTests.Services;

public sealed class CourierGatewayTests
{
    [Fact]
    public async Task CityDirectoryIncludesOfficesBeyondThePerCityLimit()
    {
        var rows = Enumerable.Range(1, 250).Select(i => new
        {
            code = i.ToString(), name = "Office " + i, shipmentTypes = new[] { "courier" },
            address = new { city = new { name = "City " + i }, fullAddress = "Street 1" }
        });
        using var handler = new StubHandler((_, _) => Json(JsonSerializer.Serialize(new { offices = rows })));
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).GetOfficeCitiesAsync(Connection(CourierProvider.Econt));
        Assert.True(result.Success);
        Assert.Equal(250, result.Offices.Select(o => o.City).Distinct().Count());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SpeedyCityDirectoryRequestsAllCountryOfficesWithoutCityOrLimit()
    {
        var rows = Enumerable.Range(1, 250).Select(i => new
        {
            id = i, name = "Office " + i, type = "OFFICE",
            address = new { countryId = 100, siteName = "City " + i, fullAddressString = "Street 1" }
        });
        using var handler = new StubHandler((message, body) =>
        {
            Assert.Equal("https://api.speedy.bg/v1/location/office", message.RequestUri!.AbsoluteUri);
            using var request = JsonDocument.Parse(body);
            Assert.False(request.RootElement.TryGetProperty("siteName", out _));
            Assert.False(request.RootElement.TryGetProperty("limit", out _));
            Assert.Equal(100, request.RootElement.GetProperty("countryId").GetInt32());
            return Json(JsonSerializer.Serialize(new { offices = rows }));
        });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).GetOfficeCitiesAsync(Connection(CourierProvider.Speedy));
        Assert.True(result.Success);
        Assert.Equal(250, result.Offices.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Econt_OnlyCalculates_UsingExactPackDimensionsAndEuroCod(bool senderPays)
    {
        using var handler = new StubHandler((message, body) =>
        {
            Assert.Equal("https://ee.econt.com/services/Shipments/LabelService.createLabel.json", message.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("Basic", message.Headers.Authorization!.Scheme);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.Equal("calculate", root.GetProperty("mode").GetString());
            Assert.False(root.TryGetProperty("requestCourierTimeFrom", out _));
            var label = root.GetProperty("label");
            Assert.Equal("1001", label.GetProperty("senderOfficeCode").GetString());
            Assert.Equal("sender-account", label.GetProperty("senderClient").GetProperty("clientNumber").GetString());
            Assert.Equal(1, label.GetProperty("packCount").GetInt32());
            Assert.Equal(0.125m, label.GetProperty("weight").GetDecimal());
            Assert.Equal(20.01m, label.GetProperty("shipmentDimensionsW").GetDecimal());
            Assert.Equal(10m, label.GetProperty("shipmentDimensionsH").GetDecimal());
            Assert.Equal(15m, label.GetProperty("shipmentDimensionsL").GetDecimal());
            Assert.False(label.TryGetProperty("packs", out _));
            Assert.Equal(80m, label.GetProperty("services").GetProperty("cdAmount").GetDecimal());
            Assert.Equal("EUR", label.GetProperty("services").GetProperty("cdCurrency").GetString());
            Assert.True(label.GetProperty("paymentReceiverAmountIsPercent").GetBoolean());
            Assert.Equal(senderPays ? 0 : 100, label.GetProperty("paymentReceiverAmount").GetInt32());
            Assert.Equal("4000", label.GetProperty("receiverAddress").GetProperty("city").GetProperty("postCode").GetString());
            return Json("""{"label":{"totalPrice":7.92,"currency":"EUR"}}""");
        });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(CourierProvider.Econt), Quote(senderPays));
        Assert.True(result.Success);
        Assert.Equal(7.92m, result.AmountEur);
        Assert.Null(result.Office);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Speedy_UsesCalculationTotalWithVat_AndRoundsDimensionsUp(bool senderPays)
    {
        using var handler = new StubHandler((message, body) =>
        {
            Assert.Equal("https://api.speedy.bg/v1/calculate", message.RequestUri!.AbsoluteUri);
            Assert.Null(message.Headers.Authorization);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.Equal("synthetic-user", root.GetProperty("userName").GetString());
            Assert.Equal(1234, root.GetProperty("sender").GetProperty("clientId").GetInt64());
            var service = root.GetProperty("service");
            Assert.Equal(505, service.GetProperty("serviceIds")[0].GetInt32());
            var cod = service.GetProperty("additionalServices").GetProperty("cod");
            Assert.Equal(80m, cod.GetProperty("amount").GetDecimal());
            Assert.Equal("EUR", cod.GetProperty("currencyCode").GetString());
            Assert.False(cod.GetProperty("includeShippingPrice").GetBoolean());
            var parcels = root.GetProperty("content").GetProperty("parcels");
            Assert.Equal(2, parcels.GetArrayLength());
            Assert.Equal(0.125m, parcels[0].GetProperty("weight").GetDecimal());
            Assert.Equal(21, parcels[0].GetProperty("size").GetProperty("width").GetInt32());
            Assert.Equal(33, parcels[1].GetProperty("size").GetProperty("depth").GetInt32());
            Assert.Equal(2, parcels[1].GetProperty("seqNo").GetInt32());
            Assert.Equal(senderPays ? "SENDER" : "RECIPIENT", root.GetProperty("payment").GetProperty("courierServicePayer").GetString());
            Assert.Equal(100, root.GetProperty("recipient").GetProperty("addressLocation").GetProperty("countryId").GetInt32());
            return Json("""{"calculations":[{"serviceId":505,"price":{"amount":6.60,"vat":1.32,"total":7.92,"currency":"EUR"}}]}""");
        });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(CourierProvider.Speedy),
            Quote(senderPays) with { Parcels = TwoParcels() });
        Assert.True(result.Success);
        Assert.Equal(7.92m, result.AmountEur);
        Assert.Null(result.Office);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Econt_OfficeListExcludesLockersOtherCountriesAndOtherCities()
    {
        using var handler = new StubHandler((message, body) =>
        {
            Assert.EndsWith("Nomenclatures/NomenclaturesService.getOffices.json", message.RequestUri!.AbsoluteUri);
            using var payload = JsonDocument.Parse(body);
            Assert.Equal("BGR", payload.RootElement.GetProperty("countryCode").GetString());
            return Json("""
                {"offices":[
                  {"code":"1","name":"Office","address":{"city":{"name":"Plovdiv","country":{"code3":"BGR"}},"fullAddress":"Main street"},"shipmentTypes":["pack"]},
                  {"code":"2","name":"Locker","isAPS":true,"address":{"city":{"name":"Plovdiv"}}},
                  {"code":"3","name":"Foreign","address":{"city":{"name":"Plovdiv","country":{"code3":"GRC"}}}},
                  {"code":"4","name":"Sofia office","address":{"city":{"name":"Sofia"}}},
                  {"code":"5","name":"Pallet-only","address":{"city":{"name":"Plovdiv"}},"shipmentTypes":["pallet"]}
                ]}
                """);
        });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).GetOfficesAsync(Connection(CourierProvider.Econt), "Plovdiv");
        Assert.True(result.Success);
        Assert.Equal("1", Assert.Single(result.Offices).Code);
    }

    [Theory]
    [InlineData("courier", true)]
    [InlineData("COURIER", true)]
    [InlineData("pack", true)]
    [InlineData("cargo", false)]
    [InlineData("pallet", false)]
    [InlineData("post", false)]
    public async Task Econt_OfficeCapabilityUsesCourierFromActualNomenclatures(string shipmentType, bool supported)
    {
        // The official getOffices example and the public demo response use
        // "courier", not the label's "pack", and may omit the city's country.
        using var handler = new StubHandler((_, _) => Json(JsonSerializer.Serialize(new
        {
            offices = new[]
            {
                new
                {
                    code = "4001", name = "Plovdiv Office", nameEn = "Plovdiv Office",
                    isAPS = false, isMPS = false,
                    address = new
                    {
                        city = new { name = "\u041f\u043b\u043e\u0432\u0434\u0438\u0432", nameEn = "Plovdiv", country = (object?)null },
                        fullAddress = "Plovdiv Main Street 1", fullAddressEn = "Plovdiv Main Street 1"
                    },
                    shipmentTypes = new[] { shipmentType }
                }
            }
        })));
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).GetOfficesAsync(Connection(CourierProvider.Econt), "Plovdiv");
        Assert.True(result.Success);
        if (supported)
        {
            var office = Assert.Single(result.Offices);
            Assert.Equal("4001", office.Code);
            Assert.Equal("\u041f\u043b\u043e\u0432\u0434\u0438\u0432", office.City);
            Assert.Equal("Plovdiv Main Street 1", office.Address);
        }
        else Assert.Empty(result.Offices);
    }

    [Fact]
    public async Task Speedy_OfficeListRequiresOfficeAndParcelCashPickupFeatures()
    {
        using var handler = new StubHandler((message, body) =>
        {
            Assert.Equal("https://api.speedy.bg/v1/location/office", message.RequestUri!.AbsoluteUri);
            using var payload = JsonDocument.Parse(body);
            Assert.Equal("OFFICE", payload.RootElement.GetProperty("officeType").GetString());
            Assert.Equal(100, payload.RootElement.GetProperty("countryId").GetInt32());
            Assert.Contains("CASH_PAYMENT", payload.RootElement.GetProperty("officeFeatures").EnumerateArray().Select(v => v.GetString()));
            return Json("""
                {"offices":[
                  {"id":1,"name":"Office","type":"OFFICE","address":{"siteName":"Plovdiv","fullAddressString":"Main street","countryId":100}},
                  {"id":2,"name":"Locker","type":"APT","address":{"siteName":"Plovdiv"}},
                  {"id":3,"name":"Foreign","type":"OFFICE","address":{"siteName":"Plovdiv","countryId":300}},
                  {"id":4,"name":"No pickup","type":"OFFICE","pickUpAllowed":false,"address":{"siteName":"Plovdiv"}},
                  {"id":5,"name":"No cash","type":"OFFICE","cashPaymentAllowed":false,"address":{"siteName":"Plovdiv"}}
                ]}
                """);
        });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).GetOfficesAsync(Connection(CourierProvider.Speedy), "Plovdiv");
        Assert.True(result.Success);
        Assert.Equal("1", Assert.Single(result.Offices).Code);
    }

    [Theory]
    [InlineData(CourierProvider.Econt)]
    [InlineData(CourierProvider.Speedy)]
    public async Task ForgedOrRetiredOfficeCannotReachCalculate(CourierProvider provider)
    {
        using var handler = new StubHandler((_, _) => Json("""{"offices":[]}"""));
        using var client = new HttpClient(handler);
        var request = Quote() with { Destination = new("Plovdiv", "", "999") };
        var result = await new CourierGateway(client).CalculateAsync(Connection(provider), request);
        Assert.False(result.Success);
        Assert.Null(result.AmountEur);
        Assert.Equal(CourierErrorCode.InvalidDestination, result.ErrorCode);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("calculate", handler.Requests[0].Uri);
        Assert.DoesNotContain("createLabel", handler.Requests[0].Uri);
    }

    [Theory]
    [InlineData(CourierProvider.Econt)]
    [InlineData(CourierProvider.Speedy)]
    public async Task ValidOfficeIsRecheckedAndOnlyOfficeIdentifierIsSentForRecipient(CourierProvider provider)
    {
        using var handler = new StubHandler((message, body) =>
        {
            if (message.RequestUri!.AbsoluteUri.Contains("getOffices"))
                return Json("""{"offices":[{"code":"11","name":"Other office","address":{"city":{"name":"Plovdiv"}}},{"code":"12","name":"Verified office","address":{"city":{"name":"Plovdiv"},"fullAddress":"Verified street 5"}}]}""");
            if (message.RequestUri.AbsoluteUri.EndsWith("location/office"))
                return Json("""{"offices":[{"id":11,"type":"OFFICE","name":"Other office","address":{"siteName":"Plovdiv"}},{"id":12,"type":"OFFICE","name":"Verified office","address":{"siteName":"Plovdiv","fullAddressString":"Verified street 5"}}]}""");
            using var payload = JsonDocument.Parse(body);
            if (provider == CourierProvider.Econt)
            {
                var label = payload.RootElement.GetProperty("label");
                Assert.Equal("12", label.GetProperty("receiverOfficeCode").GetString());
                Assert.False(label.TryGetProperty("receiverAddress", out _));
                return Json("""{"label":{"totalPrice":5,"currency":"EUR"}}""");
            }
            var recipient = payload.RootElement.GetProperty("recipient");
            Assert.Equal(12, recipient.GetProperty("pickupOfficeId").GetInt32());
            Assert.False(recipient.TryGetProperty("addressLocation", out _));
            return Json("""{"calculations":[{"serviceId":505,"price":{"total":5,"currency":"EUR"}}]}""");
        });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(provider),
            Quote() with { Destination = new("Plovdiv", "", "12") });
        Assert.True(result.Success);
        Assert.Equal(new CourierOffice("12", "Verified office", "Plovdiv", "Verified street 5"), result.Office);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(CourierProvider.Econt, false)]
    [InlineData(CourierProvider.Econt, true)]
    [InlineData(CourierProvider.Speedy, false)]
    [InlineData(CourierProvider.Speedy, true)]
    public async Task FailedQuoteDoesNotRetainPreviouslyValidatedOffice(CourierProvider provider, bool networkFailure)
    {
        using var handler = new StubHandler((message, _) =>
        {
            if (message.RequestUri!.AbsoluteUri.Contains("getOffices"))
                return Json("""{"offices":[{"code":"12","name":"Verified office","address":{"city":{"name":"Plovdiv"}}}]}""");
            if (message.RequestUri.AbsoluteUri.EndsWith("location/office"))
                return Json("""{"offices":[{"id":12,"type":"OFFICE","name":"Verified office","address":{"siteName":"Plovdiv"}}]}""");
            if (networkFailure) throw new HttpRequestException("Synthetic failure");
            return Json("""{"error":{"message":"Synthetic quote rejection"}}""");
        });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(provider),
            Quote() with { Destination = new("Plovdiv", "", "12") });
        Assert.False(result.Success);
        Assert.Null(result.AmountEur);
        Assert.Null(result.Office);
        Assert.Equal(networkFailure ? CourierErrorCode.Unavailable : CourierErrorCode.Rejected, result.ErrorCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("{}", CourierErrorCode.InvalidResponse)]
    [InlineData("not json", CourierErrorCode.InvalidResponse)]
    [InlineData("{\"label\":{\"currency\":\"EUR\"}}", CourierErrorCode.InvalidResponse)]
    [InlineData("{\"label\":{\"currency\":\"BGN\",\"totalPrice\":8}}", CourierErrorCode.UnsupportedCurrency)]
    [InlineData("{\"label\":{\"currency\":\"EUR\",\"totalPrice\":-1}}", CourierErrorCode.InvalidResponse)]
    [InlineData("{\"type\":\"Error\",\"message\":\"synthetic-secret in error\"}", CourierErrorCode.Rejected)]
    [InlineData("{\"error\":{\"message\":\"private information\"}}", CourierErrorCode.Rejected)]
    public async Task Econt_InvalidResponsesNeverBecomeZeroPriceOrExposeProviderText(string response, CourierErrorCode code)
    {
        using var handler = new StubHandler((_, _) => Json(response));
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(CourierProvider.Econt), Quote());
        Assert.False(result.Success);
        Assert.Null(result.AmountEur);
        Assert.Equal(code, result.ErrorCode);
        Assert.DoesNotContain("synthetic-secret", result.ToString());
        Assert.DoesNotContain("private information", result.ToString());
    }

    [Theory]
    [InlineData("{\"calculations\":[{\"serviceId\":999,\"price\":{\"total\":5,\"currency\":\"EUR\"}}]}", CourierErrorCode.InvalidResponse)]
    [InlineData("{\"calculations\":[{\"serviceId\":505,\"error\":{\"message\":\"private data\"}}]}", CourierErrorCode.Rejected)]
    [InlineData("{\"calculations\":[{\"serviceId\":505,\"price\":{\"total\":5,\"currency\":\"BGN\"}}]}", CourierErrorCode.UnsupportedCurrency)]
    [InlineData("{\"calculations\":[{\"serviceId\":505},{\"serviceId\":505}]}", CourierErrorCode.InvalidResponse)]
    public async Task Speedy_RequiresUniqueSuccessfulSelectedServiceInEuros(string response, CourierErrorCode code)
    {
        using var handler = new StubHandler((_, _) => Json(response));
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(CourierProvider.Speedy), Quote());
        Assert.False(result.Success);
        Assert.Null(result.AmountEur);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public async Task MissingConfigurationCannotIssueAnyRequest()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be attempted."));
        using var client = new HttpClient(handler);
        var gateway = new CourierGateway(client);
        var result = await gateway.CalculateAsync(new CourierConnection { Provider = CourierProvider.Econt }, Quote());
        Assert.Equal(CourierErrorCode.NotConfigured, result.ErrorCode);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain("synthetic-secret", Connection(CourierProvider.Econt).ToString());
    }

    [Fact]
    public async Task InvalidParcelsAndDestinationCannotIssueAnyRequest()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be attempted."));
        using var client = new HttpClient(handler);
        var gateway = new CourierGateway(client);
        var invalid = Quote() with { Parcels = [new CourierParcel(0m, 1m, 1m, 1m)] };
        Assert.Equal(CourierErrorCode.InvalidParcels,
            (await gateway.CalculateAsync(Connection(CourierProvider.Econt), invalid)).ErrorCode);
        invalid = Quote() with { Parcels = Enumerable.Repeat(new CourierParcel(1m, 1m, 1m, 1m), 501).ToArray() };
        Assert.Equal(CourierErrorCode.InvalidParcels,
            (await gateway.CalculateAsync(Connection(CourierProvider.Econt), invalid)).ErrorCode);
        invalid = Quote() with { Destination = new("Plovdiv", "4000", Address: "") };
        Assert.Equal(CourierErrorCode.InvalidDestination,
            (await gateway.CalculateAsync(Connection(CourierProvider.Econt), invalid)).ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Econt_MultipleParcelsCannotSilentlyQuoteWithoutDocumentedPackVolumeMapping()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be attempted."));
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(CourierProvider.Econt),
            Quote() with { Parcels = TwoParcels() });
        Assert.False(result.Success);
        Assert.Null(result.AmountEur);
        Assert.Equal(CourierErrorCode.UnsupportedParcels, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EcontSandboxUsesOnlyItsFixedDemoHost()
    {
        using var handler = new StubHandler((message, _) =>
        {
            Assert.Equal("https://demo.econt.com/ee/services/Nomenclatures/NomenclaturesService.getOffices.json", message.RequestUri!.AbsoluteUri);
            return Json("""{"offices":[]}""");
        });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).GetOfficesAsync(new CourierConnection
        {
            Provider = CourierProvider.Econt, UseSandbox = true, UserName = "synthetic-user", Password = "synthetic-secret"
        }, "Plovdiv");
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CourierErrorCode.Rejected)]
    [InlineData(HttpStatusCode.Redirect, CourierErrorCode.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, CourierErrorCode.Unavailable)]
    public async Task HttpErrorsRemainUnavailableWithoutProviderBody(HttpStatusCode status, CourierErrorCode error)
    {
        using var handler = new StubHandler((_, _) => new HttpResponseMessage(status)
        { Content = new StringContent("synthetic-secret and private account data") });
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(CourierProvider.Econt), Quote());
        Assert.False(result.Success);
        Assert.Null(result.AmountEur);
        Assert.Equal(error, result.ErrorCode);
        Assert.DoesNotContain("synthetic-secret", result.ToString());
    }

    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        using var handler = new StubHandler((_, _) => Json(new string('x', CourierGateway.MaxResponseBytes + 1)));
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).GetOfficesAsync(Connection(CourierProvider.Econt), "Plovdiv");
        Assert.Equal(CourierErrorCode.InvalidResponse, result.ErrorCode);
    }

    [Fact]
    public async Task NetworkFailureDoesNotExposeExceptionOrCredential()
    {
        using var handler = new StubHandler((_, _) => throw new HttpRequestException("synthetic-secret"));
        using var client = new HttpClient(handler);
        var result = await new CourierGateway(client).CalculateAsync(Connection(CourierProvider.Econt), Quote());
        Assert.Equal(CourierErrorCode.Unavailable, result.ErrorCode);
        Assert.DoesNotContain("synthetic-secret", result.ToString());
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var handler = new StubHandler((_, _) => throw new OperationCanceledException(cancellation.Token));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CourierGateway(client)
            .CalculateAsync(Connection(CourierProvider.Econt), Quote(), cancellation.Token));
    }

    [Fact]
    public void ProductionHandlerDoesNotFollowRedirectsOrPersistCookies()
    {
        using var handler = CourierGateway.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    private static CourierConnection Connection(CourierProvider provider) => new()
    {
        Provider = provider, UserName = "synthetic-user", Password = "synthetic-secret",
        SenderClientNumber = provider == CourierProvider.Econt ? "sender-account" : "1234",
        SenderOfficeCode = "1001", SenderName = "Synthetic Shop", SenderPhone = "0888888888", ServiceId = 505
    };

    private static CourierQuoteRequest Quote(bool senderPays = false) => new(
        new("Plovdiv", "4000", Address: "Main street 1"),
        [new(0.125m, 20.01m, 10m, 15m)], 80m, senderPays);
    private static IReadOnlyList<CourierParcel> TwoParcels() =>
        [new(0.125m, 20.01m, 10m, 15m), new(3.5m, 50m, 12m, 32.5m)];
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Uri, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsoluteUri, body));
            return respond(request, body);
        }
    }
}

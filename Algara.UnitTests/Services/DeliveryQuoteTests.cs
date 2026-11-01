using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.Controllers;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Shipping;
using Algara.Web.Services.Shipping.Carriers;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Moq;

namespace Algara.UnitTests.Services;

public class DeliveryQuoteTests
{
    [Theory]
    [InlineData("100", false, "8.75")]
    [InlineData("100.01", true, "0")]
    [InlineData("99.99", false, "8.75")]
    public async Task FreeCourierThresholdUsesGoodsOnlyAndIsStrictlyAboveOneHundred(string goodsText, bool senderPays, string deliveryText)
    {
        var goods = decimal.Parse(goodsText, System.Globalization.CultureInfo.InvariantCulture);
        var expected = decimal.Parse(deliveryText, System.Globalization.CultureInfo.InvariantCulture);
        var fixture = new Fixture(); fixture.ConfigureCourier();
        CourierQuoteRequest? request = null;
        fixture.Couriers.Setup(c => c.CalculateAsync(It.IsAny<CourierConnection>(), It.IsAny<CourierQuoteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CourierConnection, CourierQuoteRequest, CancellationToken>((_, value, _) => request = value)
            .ReturnsAsync(new CourierQuoteResult(true, 8.75m));
        var result = await fixture.Service.CalculateAsync(Cart(Product(goods)), DeliveryMethod.SpeedyAddress, "Plovdiv", "4000", "Test street 1", null);
        Assert.Null(result.ErrorKey);
        Assert.Equal(expected, result.Quote!.DeliveryAmount);
        Assert.Equal(goods + expected, result.Quote.Total);
        Assert.Equal(goods, request!.GoodsAmount);
        Assert.Equal(senderPays, request.SenderPays);
    }

    [Fact]
    public async Task FailedCourierLookupDoesNotBecomeFreeEvenAboveThreshold()
    {
        var fixture = new Fixture(); fixture.ConfigureCourier();
        fixture.Couriers.Setup(c => c.CalculateAsync(It.IsAny<CourierConnection>(), It.IsAny<CourierQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CourierQuoteResult.Failure(CourierErrorCode.Unavailable));
        var result = await fixture.Service.CalculateAsync(Cart(Product(200)), DeliveryMethod.SpeedyOffice, "Plovdiv", null, null, "7");
        Assert.Null(result.Quote);
        Assert.Equal("CourierFailed", result.ErrorKey);
    }

    [Fact]
    public async Task InvalidOfficeIsNotReportedAsAQuote()
    {
        var fixture = new Fixture(); fixture.ConfigureCourier();
        fixture.Couriers.Setup(c => c.CalculateAsync(It.IsAny<CourierConnection>(), It.IsAny<CourierQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CourierQuoteResult.Failure(CourierErrorCode.InvalidDestination));
        var result = await fixture.Service.CalculateAsync(Cart(Product()), DeliveryMethod.SpeedyOffice, "Plovdiv", null, null, "forged-office");
        Assert.Null(result.Quote);
        Assert.Equal("InvalidDestination", result.ErrorKey);
    }

    [Fact]
    public void ParcelListRepeatsEveryPackageForEveryActualItem()
    {
        var product = Product();
        product.ShippingParcels.Add(new() { ProductN = 1, Position = 2, WeightKg = 0.325m, WidthCm = 10, HeightCm = 20, DepthCm = 30 });
        Assert.True(DeliveryQuoteService.TryBuildParcels(Cart(product, 3), out var parcels));
        Assert.Equal(6, parcels.Count);
        Assert.Equal(3, parcels.Count(p => p.WeightKg == 2.5m));
        Assert.Equal(3, parcels.Count(p => p.WeightKg == 0.325m));
        Assert.All(parcels.Where(p => p.WeightKg == 0.325m), p => Assert.Equal(30m, p.DepthCm));
    }

    [Fact]
    public async Task MixedCartWithOneNonCourierProductBlocksCourierForTheWholeCart()
    {
        var large = Product(); large.N = 2; large.AllowsCourier = false;
        var cart = new CartSnapshot { Lines = [Line(Product()), Line(large)] };
        var fixture = new Fixture();
        Assert.False(DeliveryQuoteService.AllowsCourier(cart));
        var result = await fixture.Service.CalculateAsync(cart, DeliveryMethod.EcontOffice, "Plovdiv", null, null, "7");
        Assert.Equal("CourierNotAllowed", result.ErrorKey);
        Assert.Null(result.Quote);
        fixture.Couriers.VerifyNoOtherCalls();
        fixture.Settings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingPackageDataDoesNotFallBackToCatalogDimensionsOrZero()
    {
        var product = Product(); product.ShippingParcels.Clear();
        product.WidthCm = 10; product.HeightCm = 10; product.DepthCm = 10;
        var fixture = new Fixture();
        var result = await fixture.Service.CalculateAsync(Cart(product), DeliveryMethod.SpeedyAddress, "Plovdiv", "4000", "Street 1", null);
        Assert.Equal("IncompleteParcels", result.ErrorKey);
        Assert.Null(result.Quote);
        fixture.Couriers.VerifyNoOtherCalls();
        fixture.Settings.VerifyNoOtherCalls();
    }

    [Fact]
    public void CourierRequestSizeIsBoundedBeforeCallingTheGateway()
    {
        var product = Product(); product.ShippingParcels.Clear();
        for (var index = 1; index <= 50; index++)
            product.ShippingParcels.Add(new() { ProductN = 1, Position = index, WeightKg = 1, WidthCm = 10, HeightCm = 10, DepthCm = 10 });
        Assert.False(DeliveryQuoteService.TryBuildParcels(Cart(product, 11), out _));
        Assert.True(DeliveryQuoteService.TryBuildParcels(Cart(product, 10), out var parcels));
        Assert.Equal(CourierGateway.MaxQuoteParcels, parcels.Count);
    }

    [Fact]
    public async Task PickupWorksForLargeFurnitureWithoutShippingSettingsOrPackages()
    {
        var product = Product(); product.AllowsCourier = false; product.ShippingParcels.Clear();
        var fixture = new Fixture();
        var result = await fixture.Service.CalculateAsync(Cart(product), DeliveryMethod.Pickup, null, null, null, null);
        Assert.Equal(0m, result.Quote!.DeliveryAmount);
        Assert.Equal(product.Price, result.Quote.Total);
        Assert.False(result.Quote.IsTest);
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls(); fixture.Geocoder.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public async Task InvalidDeliveryMethodsAreRejected(int method)
    {
        var fixture = new Fixture();
        var result = await fixture.Service.CalculateAsync(Cart(Product()), (DeliveryMethod)method, "Plovdiv", "4000", "Street", null);
        Assert.Equal("InvalidMethod", result.ErrorKey);
        Assert.Null(result.Quote);
        fixture.Settings.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, "4000", "Street")]
    [InlineData("X", "4000", "Street")]
    [InlineData("Plovdiv", "invalid", "Street")]
    [InlineData("Plovdiv", "4000", null)]
    [InlineData("Plovdiv\nOther", "4000", "Street")]
    public async Task InvalidAddressCannotStartAnExternalRequest(string? city, string? post, string? address)
    {
        var fixture = new Fixture();
        var result = await fixture.Service.CalculateAsync(Cart(Product()), DeliveryMethod.SpeedyAddress, city, post, address, null);
        Assert.Equal("InvalidAddress", result.ErrorKey);
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmptyOrUnresolvedCartCannotBeQuotedEvenForPickup()
    {
        var fixture = new Fixture();
        Assert.Equal("InvalidCart", (await fixture.Service.CalculateAsync(new(), DeliveryMethod.Pickup, null, null, null, null)).ErrorKey);
        var unknown = Product(); unknown.StockQuantity = null;
        Assert.Equal("InvalidCart", (await fixture.Service.CalculateAsync(Cart(unknown), DeliveryMethod.Pickup, null, null, null, null)).ErrorKey);
        fixture.Settings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingCourierConfigurationIsAnExplicitFailure()
    {
        var fixture = new Fixture();
        fixture.Settings.Setup(s => s.GetCourierAsync(CourierProvider.Speedy, It.IsAny<CancellationToken>())).ReturnsAsync((CourierConnection?)null);
        var result = await fixture.Service.CalculateAsync(Cart(Product()), DeliveryMethod.SpeedyOffice, "Plovdiv", null, null, "7");
        Assert.Equal("CourierUnavailable", result.ErrorKey);
        Assert.Null(result.Quote);
        fixture.Couriers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(49.999999, 0)]
    [InlineData(50, 0)]
    [InlineData(50.000001, 100)]
    [InlineData(600, 100)]
    public void OwnTransportIncludesTheFiftyKilometreBoundary(double distance, int amount)
        => Assert.Equal((decimal)amount, DeliveryQuoteService.OwnTransportAmount(distance));

    [Fact]
    public void OwnTransportRejectsInvalidDistanceAndUsesRadius()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryQuoteService.OwnTransportAmount(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryQuoteService.OwnTransportAmount(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryQuoteService.OwnTransportAmount(-1));
        Assert.Equal(0, DeliveryQuoteService.DistanceKm(42, 24, 42, 24));
        Assert.InRange(DeliveryQuoteService.DistanceKm(0, 0, 1, 0), 111.19, 111.20);
    }

    [Fact]
    public async Task OwnTransportNeedsConfiguredOriginAndResolvedAddress()
    {
        var fixture = new Fixture();
        fixture.Settings.Setup(s => s.GetTransportAsync(It.IsAny<CancellationToken>())).ReturnsAsync((TransportQuoteSettings?)null);
        var result = await fixture.Service.CalculateAsync(Cart(Product()), DeliveryMethod.OwnTransport, "Plovdiv", "4000", "Street 1", null);
        Assert.Equal("TransportUnavailable", result.ErrorKey);
        Assert.Null(result.Quote);
        fixture.Geocoder.VerifyNoOtherCalls();
        fixture.Settings.Setup(s => s.GetTransportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TransportQuoteSettings(42.15m, 24.75m, "synthetic-key"));
        fixture.Geocoder.Setup(g => g.FindAsync("synthetic-key", "Plovdiv", "4000", "Street 1", It.IsAny<CancellationToken>())).ReturnsAsync((GeocodedAddress?)null);
        result = await fixture.Service.CalculateAsync(Cart(Product()), DeliveryMethod.OwnTransport, "Plovdiv", "4000", "Street 1", null);
        Assert.Equal("AddressUnresolved", result.ErrorKey);
        Assert.Null(result.Quote);
    }

    [Fact]
    public async Task OwnTransportUsesTheResolvedCoordinatesAndFixedTariff()
    {
        var fixture = new Fixture();
        fixture.Settings.Setup(s => s.GetTransportAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TransportQuoteSettings(42.15m, 24.75m, "synthetic-key"));
        fixture.Geocoder.Setup(g => g.FindAsync("synthetic-key", "Far city", "1000", "Street 1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeocodedAddress(43.15m, 24.75m, "Resolved destination"));
        var result = await fixture.Service.CalculateAsync(Cart(Product(5000)), DeliveryMethod.OwnTransport, "Far city", "1000", "Street 1", null);
        Assert.Equal(100m, result.Quote!.DeliveryAmount);
        Assert.Equal(5100m, result.Quote.Total);
        Assert.False(result.Quote.IsTest);
        Assert.Equal("Resolved destination", result.Quote.ResolvedAddress);
        Assert.InRange(result.Quote.DistanceKm!.Value, 111.19, 111.20);
        fixture.Couriers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CalculateControllerDiscardsPostedCartAndRecalculatesCurrentCatalogPrice()
    {
        var product = Product(99);
        var repository = new Mock<IProductRepository>(MockBehavior.Strict);
        repository.Setup(p => p.GetByNAsync(1)).ReturnsAsync(product);
        var store = new Mock<ICartStore>(MockBehavior.Strict);
        store.Setup(s => s.Read()).Returns(new[] { new CartEntry(1, null, 2) });
        var fixture = new Fixture();
        var controller = new ShippingController(new CartService(repository.Object, store.Object, TimeProvider.System), fixture.Service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var form = new DeliveryQuoteForm { Method = DeliveryMethod.Pickup, Cart = Cart(Product(1)) };
        var result = Assert.IsType<ViewResult>(await controller.Calculate(form));
        var returned = Assert.IsType<DeliveryQuoteForm>(result.Model);
        Assert.Equal(198m, returned.Quote!.GoodsAmount);
        product.Price = 101;
        returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await controller.Calculate(form)).Model);
        Assert.Equal(202m, returned.Quote!.GoodsAmount);
        store.Verify(s => s.Read(), Times.Exactly(2));
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FailedFormValidationClearsAnyPreviousCalculatedOutput()
    {
        var store = new Mock<ICartStore>(MockBehavior.Strict);
        store.Setup(s => s.Read()).Returns(Array.Empty<CartEntry>());
        var fixture = new Fixture();
        var controller = new ShippingController(new CartService(Mock.Of<IProductRepository>(MockBehavior.Strict), store.Object, TimeProvider.System), fixture.Service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ModelState.AddModelError(nameof(DeliveryQuoteForm.City), "Invalid city");
        var form = new DeliveryQuoteForm
        {
            Quote = new DeliveryQuote(1, 0, DateTimeOffset.UtcNow),
            Offices = [new CourierOffice("7", "Previous office", "Plovdiv", "Street")]
        };
        var result = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await controller.Calculate(form)).Model);
        Assert.Null(result.Quote);
        Assert.Empty(result.Offices);
        Assert.Equal("InvalidAddress", result.ErrorKey);
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(DeliveryQuoteForm.Cart))]
    [InlineData(nameof(DeliveryQuoteForm.Quote))]
    [InlineData(nameof(DeliveryQuoteForm.ErrorKey))]
    [InlineData(nameof(DeliveryQuoteForm.Offices))]
    public void ClientCannotBindServerCalculatedState(string property)
        => Assert.Single(typeof(DeliveryQuoteForm).GetProperty(property)!.GetCustomAttributes(typeof(BindNeverAttribute), false));

    [Theory]
    [InlineData(nameof(ShippingController.Calculate))]
    [InlineData(nameof(ShippingController.Offices))]
    public void CarrierEndpointsRequirePostAntiforgeryAndRateLimiting(string action)
    {
        var method = typeof(ShippingController).GetMethod(action)!;
        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), false));
        Assert.Single(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false));
        var rateLimit = Assert.Single(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false));
        Assert.Equal("DeliveryQuotes", ((EnableRateLimitingAttribute)rateLimit).PolicyName);
    }

    [Fact]
    public async Task OfficeLookupIsUnavailableForMixedCartOrAddressDelivery()
    {
        var fixture = new Fixture();
        var product = Product(); product.AllowsCourier = false;
        Assert.False((await fixture.Service.OfficesAsync(Cart(product), DeliveryMethod.SpeedyOffice, "Plovdiv")).Success);
        Assert.False((await fixture.Service.OfficesAsync(Cart(Product()), DeliveryMethod.SpeedyAddress, "Plovdiv")).Success);
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("   s   ")]
    [InlineData("Plovdiv\nOther")]
    public async Task OfficeLookupRejectsInvalidNormalizedCityBeforeCallingGateway(string city)
    {
        var fixture = new Fixture();
        Assert.False((await fixture.Service.OfficesAsync(Cart(Product()), DeliveryMethod.SpeedyOffice, city)).Success);
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(ShippingController.CalculatePage), nameof(ShippingController.Calculate))]
    [InlineData(nameof(ShippingController.OfficesPage), nameof(ShippingController.Offices))]
    public void LanguageSwitchGetReturnsToBlankPreviewWithoutExternalCallsOrAddressState(string methodName, string publicAction)
    {
        var repository = new Mock<IProductRepository>(MockBehavior.Strict);
        var store = new Mock<ICartStore>(MockBehavior.Strict);
        var fixture = new Fixture();
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?City=PrivateCity&Address=PrivateAddress&OfficeCode=77");
        var controller = new ShippingController(new CartService(repository.Object, store.Object, TimeProvider.System), fixture.Service)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        var method = typeof(ShippingController).GetMethod(methodName)!;
        Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), false));
        var action = Assert.IsType<ActionNameAttribute>(Assert.Single(method.GetCustomAttributes(typeof(ActionNameAttribute), false)));
        Assert.Equal(publicAction, action.Name);
        var result = Assert.IsType<RedirectToActionResult>(method.Invoke(controller, null));
        Assert.Equal(nameof(ShippingController.Index), result.ActionName);
        Assert.Null(result.ControllerName);
        Assert.Null(result.RouteValues);
        Assert.False(result.PreserveMethod);
        repository.VerifyNoOtherCalls(); store.VerifyNoOtherCalls();
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls(); fixture.Geocoder.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(CourierErrorCode.NotConfigured, "OfficeLookupUnavailable")]
    [InlineData(CourierErrorCode.Rejected, "OfficeLookupRejected")]
    [InlineData(CourierErrorCode.InvalidResponse, "OfficeLookupInvalidResponse")]
    [InlineData(CourierErrorCode.Unavailable, "OfficeLookupFailed")]
    [InlineData(CourierErrorCode.InvalidDestination, "InvalidOfficeCity")]
    public async Task OfficeControllerUsesOfficeErrorsAndDiscardsAnyPartialOrPreviousResults(CourierErrorCode failure, string expected)
    {
        var fixture = new Fixture(); fixture.ConfigureCourier();
        fixture.Couriers.Setup(c => c.GetOfficesAsync(It.IsAny<CourierConnection>(), "Plovdiv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierOfficeResult(false, [new CourierOffice("unverified", "Unverified office", "Plovdiv", "Street")], failure));
        var controller = OfficeController(fixture);
        controller.ModelState.SetModelValue(nameof(DeliveryQuoteForm.OfficeCode), "previous-office", "previous-office");
        var form = new DeliveryQuoteForm
        {
            Method = DeliveryMethod.EcontOffice, City = "Plovdiv", OfficeCode = "previous-office",
            Quote = new DeliveryQuote(80, 3, DateTimeOffset.UtcNow),
            Offices = [new CourierOffice("previous-office", "Previous office", "Plovdiv", "Street")]
        };
        var result = Assert.IsType<ViewResult>(await controller.Offices(form));
        Assert.Equal("Index", result.ViewName);
        var returned = Assert.IsType<DeliveryQuoteForm>(result.Model);
        Assert.Equal(expected, returned.ErrorKey);
        Assert.NotEqual("CourierFailed", returned.ErrorKey);
        Assert.Null(returned.Quote);
        Assert.Empty(returned.Offices);
        Assert.Null(returned.OfficeCode);
        Assert.False(controller.ModelState.ContainsKey(nameof(DeliveryQuoteForm.OfficeCode)));
        fixture.Couriers.Verify(c => c.GetOfficesAsync(It.IsAny<CourierConnection>(), "Plovdiv", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Couriers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficeLookupJsonReturnsOnlyOfficesAndSafeError(bool failure)
    {
        var fixture = new Fixture(); fixture.ConfigureCourier();
        fixture.Couriers.Setup(c => c.GetOfficesAsync(It.IsAny<CourierConnection>(), "Plovdiv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure ? CourierOfficeResult.Failure(CourierErrorCode.Unavailable) :
                new CourierOfficeResult(true, [new CourierOffice("7", "Central", "Plovdiv", "Street")]));
        var result = Assert.IsType<JsonResult>(await OfficeController(fixture).Offices(
            new DeliveryQuoteForm { Method = DeliveryMethod.EcontOffice, City = "Plovdiv" }, json: true));
        var data = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(2, data.EnumerateObject().Count());
        Assert.Equal(failure ? 0 : 1, data.GetProperty("offices").GetArrayLength());
        Assert.Equal(failure ? "OfficeLookupFailed" : null, data.GetProperty("errorKey").GetString());
        fixture.Couriers.Verify(c => c.GetOfficesAsync(It.IsAny<CourierConnection>(), "Plovdiv", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Couriers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OfficeControllerReportsMissingConfigurationWithoutCallingTheCarrier()
    {
        var fixture = new Fixture();
        fixture.Settings.Setup(s => s.GetCourierAsync(CourierProvider.Econt, It.IsAny<CancellationToken>())).ReturnsAsync((CourierConnection?)null);
        var result = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await OfficeController(fixture).Offices(
            new DeliveryQuoteForm { Method = DeliveryMethod.EcontOffice, City = "Plovdiv" })).Model);
        Assert.Equal("OfficeLookupUnavailable", result.ErrorKey);
        Assert.Empty(result.Offices);
        Assert.Null(result.Quote);
        fixture.Couriers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OfficeControllerLoadsByCityWithoutRequiringHiddenAddressOrPreviousOfficeFields()
    {
        var fixture = new Fixture(); fixture.ConfigureCourier();
        var office = new CourierOffice("7", "Central office", "Plovdiv", "Street 1");
        fixture.Couriers.Setup(c => c.GetOfficesAsync(It.IsAny<CourierConnection>(), "Plovdiv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierOfficeResult(true, [office]));
        var controller = OfficeController(fixture);
        controller.ModelState.AddModelError(nameof(DeliveryQuoteForm.PostalCode), "Hidden postal code is invalid");
        controller.ModelState.AddModelError(nameof(DeliveryQuoteForm.Address), "Hidden address is invalid");
        controller.ModelState.AddModelError(nameof(DeliveryQuoteForm.OfficeCode), "Previous office is invalid");
        var form = new DeliveryQuoteForm { Method = DeliveryMethod.EcontOffice, City = " Plovdiv ", OfficeCode = "old" };
        var returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await controller.Offices(form)).Model);
        Assert.Null(returned.ErrorKey);
        Assert.Equal(office, Assert.Single(returned.Offices));
        Assert.Null(returned.OfficeCode);
        Assert.Null(returned.Quote);
        Assert.True(controller.ModelState.IsValid);
        fixture.Couriers.Verify(c => c.GetOfficesAsync(It.IsAny<CourierConnection>(), "Plovdiv", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Couriers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OfficeControllerKeepsEmptySuccessfulResultsDistinctFromLookupFailures()
    {
        var fixture = new Fixture(); fixture.ConfigureCourier();
        fixture.Couriers.Setup(c => c.GetOfficesAsync(It.IsAny<CourierConnection>(), "Plovdiv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierOfficeResult(true, []));
        var returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await OfficeController(fixture).Offices(
            new DeliveryQuoteForm { Method = DeliveryMethod.EcontOffice, City = "Plovdiv" })).Model);
        Assert.Equal("NoOffices", returned.ErrorKey);
        Assert.Empty(returned.Offices);
        Assert.Null(returned.Quote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("s")]
    public async Task OfficeControllerAsksForCityRatherThanStreetAddressWhenCityIsMissing(string? city)
    {
        var fixture = new Fixture();
        var controller = OfficeController(fixture);
        var returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await controller.Offices(
            new DeliveryQuoteForm { Method = DeliveryMethod.EcontOffice, City = city, OfficeCode = "old" })).Model);
        Assert.Equal("InvalidOfficeCity", returned.ErrorKey);
        Assert.Null(returned.OfficeCode);
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OfficeControllerRetainsCityValidationAndClearsStaleSelection()
    {
        var fixture = new Fixture();
        var controller = OfficeController(fixture);
        controller.ModelState.AddModelError(nameof(DeliveryQuoteForm.City), "City too long");
        var returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await controller.Offices(
            new DeliveryQuoteForm { Method = DeliveryMethod.SpeedyOffice, City = new string('a', 101), OfficeCode = "old" })).Model);
        Assert.Equal("InvalidOfficeCity", returned.ErrorKey);
        Assert.Null(returned.OfficeCode);
        Assert.Empty(returned.Offices);
        Assert.True(controller.ModelState.ContainsKey(nameof(DeliveryQuoteForm.City)));
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(DeliveryMethod.Pickup)]
    [InlineData(DeliveryMethod.SpeedyAddress)]
    [InlineData((DeliveryMethod)999)]
    public async Task OfficeControllerRejectsNonOfficeMethodsBeforeLookup(DeliveryMethod method)
    {
        var fixture = new Fixture();
        var returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await OfficeController(fixture).Offices(
            new DeliveryQuoteForm { Method = method, City = "Plovdiv" })).Model);
        Assert.Equal("InvalidMethod", returned.ErrorKey);
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OfficeControllerReportsCartEligibilityWithoutBlamingTheCityOrCarrier()
    {
        var fixture = new Fixture();
        var product = Product(); product.AllowsCourier = false;
        var returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await OfficeController(fixture, product).Offices(
            new DeliveryQuoteForm { Method = DeliveryMethod.EcontOffice, City = "Plovdiv" })).Model);
        Assert.Equal("CourierNotAllowed", returned.ErrorKey);
        product.StockQuantity = null;
        returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await OfficeController(fixture, product).Offices(
            new DeliveryQuoteForm { Method = DeliveryMethod.EcontOffice, City = "Plovdiv" })).Model);
        Assert.Equal("InvalidCart", returned.ErrorKey);
        fixture.Settings.VerifyNoOtherCalls(); fixture.Couriers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("OfficeLookupUnavailable")]
    [InlineData("OfficeLookupFailed")]
    [InlineData("OfficeLookupRejected")]
    [InlineData("OfficeLookupInvalidResponse")]
    [InlineData("InvalidOfficeCity")]
    public void OfficeErrorResourcesResolveInBothLanguagesAndDifferFromPriceFailure(string key)
    {
        var resources = new System.Resources.ResourceManager("Algara.Web.Resources.Localization.ShippingResources", typeof(Algara.Web.Localization.ShippingResources).Assembly);
        var bg = resources.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("bg"));
        var en = resources.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("en"));
        Assert.False(string.IsNullOrWhiteSpace(bg));
        Assert.False(string.IsNullOrWhiteSpace(en));
        Assert.NotEqual(bg, en);
        Assert.NotEqual(resources.GetString("CourierFailed", System.Globalization.CultureInfo.GetCultureInfo("bg")), bg);
        Assert.NotEqual(resources.GetString("CourierFailed", System.Globalization.CultureInfo.GetCultureInfo("en")), en);
    }

    [Theory]
    [InlineData(false, 80)]
    [InlineData(true, 80)]
    [InlineData(false, 150)]
    [InlineData(true, 150)]
    public async Task SuccessfulCourierQuoteUsesServerEnvironmentAndIgnoresAnyPreviousClientQuote(bool sandbox, int goodsAmount)
    {
        var fixture = new Fixture(); fixture.ConfigureCourier(sandbox);
        fixture.Couriers.Setup(c => c.CalculateAsync(It.Is<CourierConnection>(connection => connection.UseSandbox == sandbox),
                It.IsAny<CourierQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierQuoteResult(true, 8.75m));
        var controller = OfficeController(fixture, Product(goodsAmount));
        var form = new DeliveryQuoteForm
        {
            Method = DeliveryMethod.EcontOffice, City = "Plovdiv", OfficeCode = "7",
            Quote = new DeliveryQuote(1, 0, DateTimeOffset.UtcNow, IsTest: !sandbox)
        };
        var returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await controller.Calculate(form)).Model);
        Assert.Null(returned.ErrorKey);
        Assert.Equal(sandbox, returned.Quote!.IsTest);
        Assert.Equal((decimal)goodsAmount, returned.Quote.GoodsAmount);
        Assert.Equal(goodsAmount > 100 ? 0m : 8.75m, returned.Quote.DeliveryAmount);
        Assert.Null(typeof(DeliveryQuoteForm).GetProperty("IsTest"));
    }

    [Fact]
    public void TestQuoteNoticeResolvesInBothLanguages()
    {
        var resources = new System.Resources.ResourceManager("Algara.Web.Resources.Localization.ShippingResources", typeof(Algara.Web.Localization.ShippingResources).Assembly);
        foreach (var key in new[] { "TestQuoteTitle", "TestQuoteNotice" })
        {
            var bg = resources.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("bg"));
            var en = resources.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("en"));
            Assert.False(string.IsNullOrWhiteSpace(bg));
            Assert.False(string.IsNullOrWhiteSpace(en));
            Assert.NotEqual(bg, en);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QuoteRetainsOnlyTheSuccessfullyVerifiedOfficeAndIgnoresPreviousClientLabel(bool success)
    {
        var verified = new CourierOffice("4033", "Verified central office", "Plovdiv", "Verified street 12");
        var fixture = new Fixture(); fixture.ConfigureCourier();
        fixture.Couriers.Setup(c => c.CalculateAsync(It.IsAny<CourierConnection>(), It.IsAny<CourierQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierQuoteResult(success, success ? 4.39m : null,
                success ? null : CourierErrorCode.Rejected, verified));
        var controller = OfficeController(fixture);
        var form = new DeliveryQuoteForm
        {
            Method = DeliveryMethod.EcontOffice, City = "Plovdiv", OfficeCode = "4033",
            Quote = new DeliveryQuote(1, 0, DateTimeOffset.UtcNow,
                Office: new CourierOffice("4033", "Untrusted previous label", "Plovdiv", "Untrusted address"))
        };
        var returned = Assert.IsType<DeliveryQuoteForm>(Assert.IsType<ViewResult>(await controller.Calculate(form)).Model);
        if (success)
        {
            Assert.Null(returned.ErrorKey);
            Assert.Same(verified, returned.Quote!.Office);
            Assert.Equal("Verified central office", returned.Quote.Office!.Name);
            Assert.Equal("Verified street 12", returned.Quote.Office.Address);
        }
        else
        {
            Assert.Null(returned.Quote);
            Assert.Equal("CourierFailed", returned.ErrorKey);
        }
        Assert.Empty(returned.Offices);
        fixture.Couriers.Verify(c => c.CalculateAsync(It.IsAny<CourierConnection>(), It.IsAny<CourierQuoteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Couriers.VerifyNoOtherCalls();
    }

    private static ShippingController OfficeController(Fixture fixture, Product? product = null)
    {
        product ??= Product();
        var repository = new Mock<IProductRepository>(MockBehavior.Strict);
        repository.Setup(p => p.GetByNAsync(product.N)).ReturnsAsync(product);
        var store = new Mock<ICartStore>(MockBehavior.Strict);
        store.Setup(s => s.Read()).Returns(new[] { new CartEntry(product.N, null, 1) });
        return new ShippingController(new CartService(repository.Object, store.Object, TimeProvider.System), fixture.Service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static Product Product(decimal price = 80) => new()
    {
        N = 1, Name = "Lamp", Sku = "LAMP", Price = price, StockQuantity = 99, AllowsCourier = true,
        ShippingParcels = [new() { ProductN = 1, Position = 1, WeightKg = 2.5m, WidthCm = 20, HeightCm = 30, DepthCm = 40 }]
    };
    private static CartLine Line(Product product, int quantity = 1)
        => CartService.Evaluate(new(product.N, null, quantity), product, DateTime.Now);
    private static CartSnapshot Cart(Product product, int quantity = 1) => new() { Lines = [Line(product, quantity)] };
    private sealed class Fixture
    {
        public Mock<IDeliverySettingsReader> Settings { get; } = new(MockBehavior.Strict);
        public Mock<ICourierGateway> Couriers { get; } = new(MockBehavior.Strict);
        public Mock<IAddressGeocoder> Geocoder { get; } = new(MockBehavior.Strict);
        public DeliveryQuoteService Service => new(Settings.Object, Couriers.Object, Geocoder.Object, TimeProvider.System);
        public void ConfigureCourier(bool useSandbox = false) => Settings.Setup(s => s.GetCourierAsync(It.IsAny<CourierProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CourierProvider provider, CancellationToken _) => new CourierConnection { Provider = provider, UseSandbox = useSandbox });
    }
}

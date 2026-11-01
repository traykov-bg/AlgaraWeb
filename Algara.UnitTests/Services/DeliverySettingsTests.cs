using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.UnitTests.Controllers;
using Algara.Web.Authorization;
using Algara.Web.Controllers;
using Algara.Web.Services.Shipping;
using Algara.Web.Services.Shipping.Carriers;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace Algara.UnitTests.Services;

public sealed class DeliverySettingsTests
{
    [Fact]
    public void DisabledSettingsDoNotInventOriginCoordinates()
    {
        var form = new DeliverySettingsForm();
        Assert.Null(DeliverySettingsService.Validate(form));
        Assert.False(form.Econt.Enabled);
        Assert.False(form.Speedy.Enabled);
        Assert.False(form.Transport.GeocodingEnabled);
        Assert.Null(form.Transport.OriginLatitude);
        Assert.Null(form.Transport.OriginLongitude);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("AAAAAAAAAAB=")]
    [InlineData(" AAAAAAAAAAA=")]
    [InlineData("AQIDBA==")]
    public async Task InvalidVersionRejectsBeforeDatabaseAndDiscardsAllSubmittedSecrets(string? version)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var form = SecretForm();
        form.Econt.ExpectedVersion = version!;
        var error = await new DeliverySettingsService(db, Protector()).SaveAsync(new ClaimsPrincipal(), form);
        Assert.NotEmpty(error!);
        Assert.DoesNotContain("synthetic", error!, StringComparison.Ordinal);
        Assert.Null(form.Econt.Password);
        Assert.Null(form.Speedy.Password);
        Assert.Null(form.Transport.GeocodingKey);
    }

    [Theory]
    [InlineData("Econt")]
    [InlineData("Speedy")]
    [InlineData("Transport")]
    public async Task MissingSectionCannotThrowOrRetainOtherSecrets(string section)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var form = SecretForm();
        typeof(DeliverySettingsForm).GetProperty(section)!.SetValue(form, null);
        var error = await new DeliverySettingsService(db, Protector()).SaveAsync(new ClaimsPrincipal(), form);
        Assert.NotEmpty(error!);
        Assert.Null(form.Econt?.Password);
        Assert.Null(form.Speedy?.Password);
        Assert.Null(form.Transport?.GeocodingKey);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("synthetic\nkey")]
    [InlineData("synthetic key")]
    [InlineData("synthetic\0key")]
    public void KeyRejectsWhitespaceAndControlCharacters(string key)
    {
        var form = new DeliverySettingsForm();
        form.Transport.GeocodingKey = key;
        Assert.NotNull(DeliverySettingsService.Validate(form));
    }

    [Fact]
    public void CoordinatesMustBeAPairWithinRangeAndCannotBeRounded()
    {
        var form = new DeliverySettingsForm();
        form.Transport.OriginLatitude = 42.1234567m;
        Assert.NotNull(DeliverySettingsService.Validate(form));
        form.Transport.OriginLongitude = 24.1234567m;
        Assert.Null(DeliverySettingsService.Validate(form));
        form.Transport.OriginLongitude = 24.12345678m;
        Assert.NotNull(DeliverySettingsService.Validate(form));
        form.Transport.OriginLongitude = 181;
        Assert.NotNull(DeliverySettingsService.Validate(form));
        form.Transport.OriginLongitude = 24;
        form.Transport.OriginLatitude = -91;
        Assert.NotNull(DeliverySettingsService.Validate(form));
    }

    [Fact]
    public void RemovingAndReplacingASecretTogetherIsRejected()
    {
        var form = new DeliverySettingsForm();
        form.Econt.Password = "synthetic-password";
        form.Econt.RemovePassword = true;
        Assert.NotNull(DeliverySettingsService.Validate(form));
        form.Econt.Password = null;
        Assert.Null(DeliverySettingsService.Validate(form));
        form.Transport.GeocodingKey = "synthetic-key";
        form.Transport.RemoveGeocodingKey = true;
        Assert.NotNull(DeliverySettingsService.Validate(form));
    }

    [Fact]
    public void CredentialLengthsMatchGatewayLimits()
    {
        var form = new DeliverySettingsForm();
        form.Econt.UserName = new string('u', 200);
        form.Econt.Password = new string('p', 1000);
        Assert.Null(DeliverySettingsService.Validate(form));
        form.Econt.UserName += "u";
        Assert.NotNull(DeliverySettingsService.Validate(form));
        form.Econt.UserName = "user";
        form.Econt.Password += "p";
        Assert.NotNull(DeliverySettingsService.Validate(form));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EnvironmentChangeRequiresPasswordReentryOrRemovalEvenWhenDisabled(bool previousSandbox, bool enabled)
    {
        var saved = new CourierConfiguration { UseSandbox = previousSandbox, UserName = "account", ProtectedPassword = "stored-ciphertext" };
        var entry = new CourierSettingsForm { UseSandbox = !previousSandbox, Enabled = enabled, UserName = "account" };
        Assert.NotNull(DeliverySettingsService.ValidateCredentialTransition(saved, entry));
        entry.Password = "newly-entered-secret";
        Assert.Null(DeliverySettingsService.ValidateCredentialTransition(saved, entry));
        entry.Password = null;
        entry.RemovePassword = true;
        Assert.Null(DeliverySettingsService.ValidateCredentialTransition(saved, entry));
    }

    [Fact]
    public void UnchangedProfileRetainsPasswordButAnotherUserCannotInheritIt()
    {
        var saved = new CourierConfiguration { UserName = "account", UseSandbox = true, ProtectedPassword = "stored-ciphertext" };
        var entry = new CourierSettingsForm { UserName = " account ", UseSandbox = true };
        Assert.Null(DeliverySettingsService.ValidateCredentialTransition(saved, entry));
        entry.UserName = "another-account";
        Assert.NotNull(DeliverySettingsService.ValidateCredentialTransition(saved, entry));
        saved.ProtectedPassword = null;
        Assert.Null(DeliverySettingsService.ValidateCredentialTransition(saved, entry));
        Assert.Null(DeliverySettingsService.ValidateCredentialTransition(null, entry));
    }

    [Fact]
    public async Task SpeedySandboxIsRejectedBeforeDatabaseAndSecretsAreDiscarded()
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var form = SecretForm();
        form.Speedy.UseSandbox = true;
        var error = await new DeliverySettingsService(db, Protector()).SaveAsync(new ClaimsPrincipal(), form);
        Assert.Contains("Speedy", error);
        Assert.Null(form.Econt.Password);
        Assert.Null(form.Speedy.Password);
        Assert.Null(form.Transport.GeocodingKey);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void PublicDemoUsernameRequiresEcontSandboxWhenEnabled(bool enabled, bool sandbox, bool accepted)
    {
        var form = new DeliverySettingsForm();
        form.Econt.UserName = " IASP-DEV ";
        form.Econt.Enabled = enabled;
        form.Econt.UseSandbox = sandbox;
        var error = DeliverySettingsService.Validate(form);
        Assert.Equal(accepted, error == null);
        if (!accepted) Assert.Contains("iasp-dev", error);
    }

    [Theory]
    [InlineData("demo.econt.com/ee/services/")]
    [InlineData("https://ee.econt.com/services/")]
    [InlineData("demo.econt.com\\ee\\services")]
    public async Task ApiAddressCannotBeSavedAsUsername(string username)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var form = SecretForm();
        form.Econt.UserName = username;
        var error = await new DeliverySettingsService(db, Protector()).SaveAsync(new ClaimsPrincipal(), form);
        Assert.Contains("API", error);
        Assert.DoesNotContain(username, error);
        Assert.Null(form.Econt.Password);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistedEnvironmentReachesAdminFormAndCarrierConnectionWithoutRevealingPassword(bool sandbox)
    {
        var protector = Protector();
        var saved = ConfiguredCourier(protector, sandbox);
        using var db = new SettingsReadContext(saved);
        var service = new DeliverySettingsService(db, protector);
        var form = await service.GetFormAsync();
        Assert.Equal(sandbox, form.Econt.UseSandbox);
        Assert.True(form.Econt.HasReadablePassword);
        Assert.Null(form.Econt.Password);
        var connection = await service.GetCourierAsync(CourierProvider.Econt);
        Assert.NotNull(connection);
        Assert.Equal(sandbox, connection.UseSandbox);
        Assert.Equal("synthetic-password", connection.Password);
    }

    [Theory]
    [InlineData(1, false, "iasp-dev")]
    [InlineData(1, false, "demo.econt.com/ee/services/")]
    [InlineData(2, true, "account")]
    public async Task LegacyUnsafeConfigurationCannotProduceCarrierConnection(int provider, bool sandbox, string username)
    {
        var protector = Protector();
        var saved = ConfiguredCourier(protector, sandbox);
        saved.Id = provider;
        saved.UserName = username;
        using var db = new SettingsReadContext(saved);
        Assert.Null(await new DeliverySettingsService(db, protector).GetCourierAsync((CourierProvider)provider));
    }

    [Fact]
    public void SettingsAuditRecordsEnvironmentWithoutUsernameOrSecrets()
    {
        var saved = ConfiguredCourier(Protector(), true);
        var audit = (string)typeof(DeliverySettingsService).GetMethod("Audit", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new[] { saved }, null])!;
        using var parsed = JsonDocument.Parse(audit);
        Assert.True(parsed.RootElement.GetProperty("Couriers")[0].GetProperty("UseSandbox").GetBoolean());
        Assert.DoesNotContain(saved.UserName, audit);
        Assert.DoesNotContain(saved.ProtectedPassword!, audit);
        Assert.DoesNotContain("synthetic-password", audit);
    }

    [Fact]
    public void SecretRoundTripsWithoutPlaintextStorageOrDiagnosticExposure()
    {
        var protector = Protector();
        const string password = "synthetic-secret-only";
        var encrypted = protector.Protect(password);
        Assert.NotEqual(password, encrypted);
        Assert.DoesNotContain(password, encrypted);
        Assert.True(protector.TryUnprotect(encrypted, out var plain));
        Assert.Equal(password, plain);
        Assert.DoesNotContain(password, new TransportQuoteSettings(42, 24, password).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-encrypted")]
    public void InvalidCiphertextReturnsNoPlaintext(string? encrypted)
    {
        Assert.False(Protector().TryUnprotect(encrypted, out var plain));
        Assert.Empty(plain);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("password\0")]
    public void ProtectedButUnusableSecretsAreNotReadable(string value)
    {
        var protector = Protector();
        Assert.False(protector.TryUnprotect(protector.Protect(value), out var plain));
        Assert.Empty(plain);
    }

    [Fact]
    public void WrongKeyRingAndOtherPurposeCannotDecryptDeliverySecrets()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = new DeliverySecretProtector(provider);
        Assert.False(Protector().TryUnprotect(protector.Protect("synthetic-secret"), out var wrongRing));
        Assert.Empty(wrongRing);
        var ciphertext = provider.CreateProtector("Algara.UnrelatedPurpose").Protect("synthetic-secret");
        Assert.False(protector.TryUnprotect(ciphertext, out var wrongPurpose));
        Assert.Empty(wrongPurpose);
    }

    [Fact]
    public void EndpointRequiresAdminPermissionPostAntiforgeryAndNoStore()
    {
        var controller = typeof(AdminDeliverySettingsController);
        Assert.Contains(PermissionCatalog.Settings.Manage, controller.GetCustomAttributes<RequirePermissionAttribute>().SelectMany(x => x.Permissions));
        Assert.True(PermissionCatalog.Find(PermissionCatalog.Settings.Manage)!.AdminOnly);
        Assert.True(controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        var action = controller.GetMethod(nameof(AdminDeliverySettingsController.Index), [typeof(DeliverySettingsForm)])!;
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Theory]
    [InlineData(StaffRoles.Moderator)]
    [InlineData(StaffRoles.CatalogEditor)]
    [InlineData(StaffRoles.OrderManager)]
    public async Task ForgedGrantAndAdminCookieRoleCannotBypassLiveAdminRequirement(string role)
    {
        using var db = new StaffAccessTestContext(role, [PermissionCatalog.Settings.Manage]);
        var actor = StaffAccessTestContext.Principal();
        ((ClaimsIdentity)actor.Identity!).AddClaim(new Claim(ClaimTypes.Role, StaffRoles.Admin));
        Assert.False(await new StaffAccessService(db).HasPermissionAsync(actor, PermissionCatalog.Settings.Manage));
    }

    [Fact]
    public void SqlMappingUsesRowVersionsSingletonAndExactCoordinatePrecision()
    {
        using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=(local);Database=NeverOpenedDeliveryUnitTest;Integrated Security=True;TrustServerCertificate=True").Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var courier = model.FindEntityType(typeof(CourierConfiguration))!;
        var transport = model.FindEntityType(typeof(TransportConfiguration))!;
        Assert.True(courier.FindProperty(nameof(CourierConfiguration.RowVersion))!.IsConcurrencyToken);
        Assert.True(transport.FindProperty(nameof(TransportConfiguration.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, courier.FindProperty(nameof(CourierConfiguration.RowVersion))!.ValueGenerated);
        Assert.Equal(7, transport.FindProperty(nameof(TransportConfiguration.OriginLatitude))!.GetScale());
        Assert.Contains(courier.GetCheckConstraints(), check => check.Name == "CK_CourierConfigurations_Provider");
        Assert.Contains(courier.GetCheckConstraints(), check => check.Name == "CK_CourierConfigurations_Sandbox");
        Assert.Equal(false, courier.FindProperty(nameof(CourierConfiguration.UseSandbox))!.GetDefaultValue());
        Assert.Contains(transport.GetCheckConstraints(), check => check.Name == "CK_TransportConfigurations_Singleton");
        Assert.Contains(transport.GetCheckConstraints(), check => check.Name == "CK_TransportConfigurations_EnabledSecret");
    }

    private static DeliverySecretProtector Protector() => new(new EphemeralDataProtectionProvider());
    private static CourierConfiguration ConfiguredCourier(DeliverySecretProtector protector, bool sandbox) => new()
    {
        Id = 1, Enabled = true, UseSandbox = sandbox, UserName = "synthetic-account",
        ProtectedPassword = protector.Protect("synthetic-password"), SenderClientNumber = "123456",
        SenderOfficeCode = "1000", SenderName = "Synthetic sender", SenderPhone = "0888000000"
    };

    private sealed class SettingsReadContext : IdentityDbContext
    {
        public SettingsReadContext(CourierConfiguration row) : base(new DbContextOptions<IdentityDbContext>())
        {
            CourierConfigurations = ReadSet(new[] { row });
            TransportConfigurations = ReadSet(Array.Empty<TransportConfiguration>());
            AccessAuditEvents = ReadSet(Array.Empty<AccessAuditEvent>());
        }

        private static DbSet<T> ReadSet<T>(IEnumerable<T> rows) where T : class
        {
            var query = new AsyncRows<T>(rows);
            var set = new Mock<DbSet<T>>();
            set.As<IQueryable<T>>().SetupGet(s => s.Provider).Returns(((IQueryable<T>)query).Provider);
            set.As<IQueryable<T>>().SetupGet(s => s.Expression).Returns(((IQueryable<T>)query).Expression);
            set.As<IQueryable<T>>().SetupGet(s => s.ElementType).Returns(typeof(T));
            set.As<IQueryable<T>>().Setup(s => s.GetEnumerator()).Returns(() => ((IEnumerable<T>)query).GetEnumerator());
            set.As<IAsyncEnumerable<T>>().Setup(s => s.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => query.GetAsyncEnumerator(token));
            return set.Object;
        }
    }

    private sealed class AsyncRows<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public AsyncRows(IEnumerable<T> rows) : base(rows) { }
        public AsyncRows(Expression expression) : base(expression) { }
        IQueryProvider IQueryable.Provider => new AsyncProvider(this);
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new AsyncEnumerator<T>(this.AsEnumerable().GetEnumerator(), cancellationToken);
    }

    private sealed class AsyncEnumerator<T>(IEnumerator<T> inner, CancellationToken token) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;
        public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
        public ValueTask<bool> MoveNextAsync() { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(inner.MoveNext()); }
    }

    private sealed class AsyncProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => (IQueryable)Activator.CreateInstance(typeof(AsyncRows<>).MakeGenericType(expression.Type.GetGenericArguments().Single()), expression)!;
        public IQueryable<T> CreateQuery<T>(Expression expression) => new AsyncRows<T>(expression);
        public object? Execute(Expression expression) => inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);
        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = inner.Execute(expression);
            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(TResult).GetGenericArguments().Single()).Invoke(null, [result])!;
        }
    }
    private static DeliverySettingsForm SecretForm() => new()
    {
        Econt = new() { Password = "synthetic-econt-password" },
        Speedy = new() { Password = "synthetic-speedy-password" },
        Transport = new() { GeocodingKey = "synthetic-geocoding-key" }
    };
}

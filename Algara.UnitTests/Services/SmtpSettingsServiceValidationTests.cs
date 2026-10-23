using System.Security.Claims;
using Algara.Identity.Data;
using Algara.Web.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Algara.UnitTests.Services;

public sealed class SmtpSettingsServiceValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAB=")]
    public async Task InvalidSaveVersion_RejectsBeforeDatabaseAccessAndDiscardsSubmittedSecret(string? version)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var secrets = new Mock<ISmtpSecretProtector>(MockBehavior.Strict);
        var probe = new Mock<ISmtpConnectionProbe>(MockBehavior.Strict);
        var service = new SmtpSettingsService(db, secrets.Object, probe.Object, new ConfigurationBuilder().Build());
        var update = ValidUpdate();
        update.ExpectedVersion = version;
        var result = await service.SaveAsync(new ClaimsPrincipal(), update);
        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.NotEmpty(result.Error!);
        Assert.DoesNotContain("synthetic-test-password", result.Error!);
        Assert.Null(update.Password);
        secrets.VerifyNoOtherCalls();
        probe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidSettings_DiscardSubmittedSecretEvenWithoutAnActorOrDatabase()
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var secrets = new Mock<ISmtpSecretProtector>(MockBehavior.Strict);
        var probe = new Mock<ISmtpConnectionProbe>(MockBehavior.Strict);
        var service = new SmtpSettingsService(db, secrets.Object, probe.Object, new ConfigurationBuilder().Build());
        var update = ValidUpdate();
        update.FromEmail = "injected@example.test\r\nBcc: another@example.test";
        var result = await service.SaveAsync(new ClaimsPrincipal(), update);
        Assert.False(result.Success);
        Assert.Null(update.Password);
        Assert.DoesNotContain("synthetic-test-password", result.Error!);
        secrets.VerifyNoOtherCalls();
        probe.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("AAAAAAAAAAB=")]
    public async Task ConnectionCheck_RequiresAnExistingCanonicalVersionBeforeAnySecretOrNetworkUse(string? version)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var secrets = new Mock<ISmtpSecretProtector>(MockBehavior.Strict);
        var probe = new Mock<ISmtpConnectionProbe>(MockBehavior.Strict);
        var service = new SmtpSettingsService(db, secrets.Object, probe.Object, new ConfigurationBuilder().Build());
        var result = await service.CheckConnectionAsync(new ClaimsPrincipal(), version);
        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.NotEmpty(result.Error!);
        secrets.VerifyNoOtherCalls();
        probe.VerifyNoOtherCalls();
    }

    private static SmtpSettingsUpdate ValidUpdate() => new()
    {
        Host = "smtp.example.test", Port = 587, Username = "sender@example.test",
        FromEmail = "sender@example.test", FromName = "Test sender", ExpectedVersion = "none",
        Password = "synthetic-test-password"
    };
}

using Algara.Identity.Services;
using Algara.Web.Services.Email;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Algara.UnitTests.Services;

public sealed class AccountEmailRulesTests
{
    [Fact]
    public void IssuedTokens_AreIndependentCanonicalAndStoreOnlyHash()
    {
        var first = AccountEmailRules.Issue();
        var second = AccountEmailRules.Issue();
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.NotEqual(first.LinkValue, second.LinkValue);
        Assert.Equal(76, first.LinkValue.Length);
        Assert.True(AccountEmailRules.TryParse(first.LinkValue, out var id, out var secret));
        Assert.Equal(first.Id, id);
        Assert.True(AccountEmailRules.MatchesHash(secret, first.Hash));
        Assert.False(AccountEmailRules.MatchesHash(secret, second.Hash));
        secret[0] ^= 1;
        Assert.False(AccountEmailRules.MatchesHash(secret, first.Hash));
        Assert.DoesNotContain(first.LinkValue, first.ToString()!);
    }

    [Fact]
    public void TokenParser_RejectsAlternativeEncodingAndMalformedInputs()
    {
        var token = AccountEmailRules.Issue().LinkValue;
        foreach (var invalid in new[] { "", token + "=", token + " ", " " + token, "#" + token,
            token[..32].ToUpperInvariant() + token[32..], token.Replace('.', '/'), token[..33] + new string('!', 43),
            "not-a-guid" + token[10..] })
            Assert.False(AccountEmailRules.TryParse(invalid, out _, out _));
        Assert.False(AccountEmailRules.TryParse(null, out _, out _));
        Assert.False(AccountEmailRules.MatchesHash(new byte[32], new string('z', 64)));
        Assert.False(AccountEmailRules.MatchesHash(new byte[31], new string('0', 64)));
    }

    [Theory]
    [InlineData(" Test@Example.test ", "test@example.test")]
    [InlineData("first+tag@example.test", "first+tag@example.test")]
    [InlineData("Name <test@example.test>", null)]
    [InlineData("test@example.test\r\nBcc: other@example.test", null)]
    [InlineData("a@example.test,b@example.test", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Email_IsOneMailboxWithoutHeaders(string? value, string? expected) =>
        Assert.Equal(expected, AccountEmailRules.NormalizeEmail(value));

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void PasswordBoundary_PreservesExistingMinimumWithoutAddedComplexity(int length, bool valid) =>
        Assert.Equal(valid, AccountEmailRules.ValidPassword(new string('a', length)));

    [Fact]
    public void PasswordCodec_UsesFreshSaltAndExistingLoginFormat()
    {
        var first = AccountPasswordCodec.Create("парола тест");
        var second = AccountPasswordCodec.Create("парола тест");
        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.Equal(16, Convert.FromBase64String(first.Salt).Length);
        var expected = KeyDerivation.Pbkdf2("парола тест", Convert.FromBase64String(first.Salt),
            KeyDerivationPrf.HMACSHA256, 10000, 32);
        Assert.Equal(Convert.ToBase64String(expected), first.Hash);
    }
}

using Algara.Web.Helpers;

namespace Algara.UnitTests.Helpers;

public class SlugHelperTests
{
    [Theory]
    [InlineData("Мека мебел", "meka-mebel")]
    [InlineData("Ъглови дивани", "aglovi-divani")]
    [InlineData("Детски шкафчета и етажерки", "detski-shkafcheta-i-etazherki")]
    [InlineData("Диван SOHO 2026", "divan-soho-2026")]
    [InlineData("  Мека__--  мебел - ", "meka-mebel")]
    [InlineData("Стол „Юлия“!", "stol-yuliya")]
    public void Generate_builds_category_slugs_from_display_names(string name, string expected)
    {
        Assert.Equal(expected, SlugHelper.Generate(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void Generate_returns_empty_for_missing_names(string? name)
    {
        Assert.Equal(string.Empty, SlugHelper.Generate(name!));
    }
}

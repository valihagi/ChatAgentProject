using ChatAgent.Api.Agent;

namespace ChatAgent.Tests;

public class NetVolumeTests
{
    [Theory]
    [InlineData("0,75 l", 750, "0,75 l")]
    [InlineData("0.75L", 750, "0.75 l")]
    [InlineData(" 75 CL ", 750, "75 cl")]
    [InlineData("330ml", 330, "330 ml")]
    [InlineData("1,5 l", 1500, "1,5 l")]
    public void Parses_and_canonicalizes(string input, double milliliters, string canonical)
    {
        Assert.True(NetVolume.TryParse(input, out var ml, out var text));
        Assert.Equal((milliliters, canonical), (ml, text));
    }

    [Theory]
    [InlineData("0,75")]
    [InlineData("16 oz")]
    [InlineData("1.000 ml")]
    [InlineData("-1 l")]
    [InlineData("0 ml")]
    [InlineData("101 l")]
    [InlineData("")]
    public void Rejects_unusable_text(string input) =>
        Assert.False(NetVolume.TryParse(input, out _, out _, out var error) || error is null);

    [Fact]
    public void Normalize_leaves_unparseable_text_visible_for_the_validator() =>
        Assert.Equal("16 oz", NetVolume.Normalize("16 oz"));
}

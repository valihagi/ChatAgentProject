using ChatAgent.Api.Agent;

namespace ChatAgent.Tests;

public class LabelSpecTests
{
    [Theory]
    [InlineData("4006 3813 33931", "4006381333931")]
    [InlineData("4006-3813-33931", "4006381333931")]
    [InlineData("   ", null)]
    [InlineData("-", null)]
    public void Numbers_lose_spaces_and_hyphens_and_empty_becomes_null(string input, string? expected) =>
        Assert.Equal(expected, new LabelSpec { Gtin = input, Sscc = input }.Normalized().Gtin);

    [Theory]
    [InlineData("Consumer_Unit", "consumer_unit")]
    [InlineData("consumer-unit", "consumer_unit")]
    [InlineData("Case", "case")]
    public void Packaging_level_is_canonical(string input, string expected) =>
        Assert.Equal(expected, new LabelSpec { PackagingLevel = input }.Normalized().PackagingLevel);

    [Theory]
    [InlineData("gs1-128", "GS1-128")]
    [InlineData("ean13", "EAN13")]
    [InlineData("Nope", "Nope")]   // unknown values stay visible so the validator can report them
    public void Symbology_gets_its_canonical_spelling(string input, string expected) =>
        Assert.Equal(expected, new LabelSpec { Symbology = input }.Normalized().Symbology);
}

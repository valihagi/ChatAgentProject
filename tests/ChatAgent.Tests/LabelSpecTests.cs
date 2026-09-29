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

    private static readonly LabelSpec Known = new()
    {
        ProductName = "Cola", PackagingLevel = "case", Symbology = "GS1-128", Gtin = "15449000000993", Batch = "L1", ItemCount = 24,
    };

    [Fact]
    public void Merge_keeps_known_values_the_model_dropped_and_applies_new_ones()
    {
        var update = new LabelSpec { BestBefore = "2027-01-31", Batch = "L2" }; // everything else null

        var merged = LabelSpec.Merge(Known, update, []);

        Assert.Equal(("Cola", "15449000000993", 24), (merged.ProductName, merged.Gtin, merged.ItemCount));
        Assert.Equal(("L2", "2027-01-31"), (merged.Batch, merged.BestBefore));
    }

    [Fact]
    public void Merge_removes_only_fields_listed_as_cleared_case_insensitively()
    {
        var merged = LabelSpec.Merge(Known, new LabelSpec(), ["batch", "ItemCount", "unknownField"]);

        Assert.Equal((null, null), (merged.Batch, merged.ItemCount));
        Assert.Equal("Cola", merged.ProductName);
    }

    [Fact]
    public void Merge_without_previous_state_is_just_the_update() =>
        Assert.Equal("Cola", LabelSpec.Merge(null, Known, []).ProductName);

    [Fact]
    public void Field_names_are_camel_case_json_names()
    {
        Assert.Contains("productName", LabelSpec.FieldNames);
        Assert.Contains("allowPastDate", LabelSpec.FieldNames);
    }
}

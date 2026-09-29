using ChatAgent.Api.Agent;

namespace ChatAgent.Tests;

public class LabelValidatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);
    private const string Gtin13 = "4006381333931";   // valid EAN-13
    private const string Gtin14 = "04006381333931";

    private static LabelSpec Bottle(Func<LabelSpec, LabelSpec>? change = null)
    {
        var spec = new LabelSpec
        {
            ProductName = "Apfelsaft", PackagingLevel = "consumer_unit", Symbology = "EAN13", Gtin = Gtin13,
        };
        return change?.Invoke(spec) ?? spec;
    }

    private static string Sscc()
    {
        const string body = "34012345000000001";
        return body + Gs1.CheckDigit(body);
    }

    private static ValidationResult Check(LabelSpec s) => LabelValidator.Validate(s, Today);

    private static void AssertIssue(ValidationResult r, string field, string kind) =>
        Assert.Contains(r.Issues, i => i.Field == field && i.Kind == kind);

    [Fact]
    public void Valid_consumer_unit_builds_ean13_request_at_300_dpi()
    {
        var r = Check(Bottle());

        Assert.True(r.Ok);
        Assert.Equal("EAN13", r.Request!.Code);
        Assert.Equal(Gtin13, r.Request.Data);
        Assert.Equal(300, r.Request.Dpi);
    }

    [Theory]
    [InlineData("EAN13", "4006381333931", 0.33)]
    [InlineData("GS1-128", "4006381333931", 0.25)]
    [InlineData("GS1DigitalLink_QRCode", "4006381333931", 0.5)]
    [InlineData("GS1DataMatrix", "4006381333931", 0.5)]
    public void Without_explicit_size_a_module_width_in_mm_fixes_the_physical_size(string symbology, string gtin, double expected)
    {
        var r = Check(Bottle(s => s with { Symbology = symbology, Gtin = gtin }));

        Assert.Equal(("mm", expected), (r.Request!.Unit, r.Request.ModuleWidth));
        Assert.Null(r.Request.Width);
    }

    [Fact]
    public void Ean13_accepts_12_digits_because_the_api_adds_the_check_digit() =>
        Assert.True(Check(Bottle(s => s with { Gtin = Gtin13[..12] })).Ok);

    [Fact]
    public void Wrong_check_digit_is_reported_with_the_expected_digit()
    {
        var r = Check(Bottle(s => s with { Gtin = "4006381333932" }));

        AssertIssue(r, "gtin", "invalid");
        Assert.Contains("expected 1", r.Issues.Single().Detail);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("40063813339AB")]
    public void Malformed_gtin_is_invalid(string gtin) =>
        AssertIssue(Check(Bottle(s => s with { Gtin = gtin })), "gtin", "invalid");

    [Fact]
    public void Missing_required_fields_are_all_reported()
    {
        var r = Check(new LabelSpec());

        AssertIssue(r, "productName", "missing");
        AssertIssue(r, "packagingLevel", "missing");
        AssertIssue(r, "symbology", "missing");
    }

    [Fact]
    public void Unsupported_symbology_is_invalid() =>
        AssertIssue(Check(Bottle(s => s with { Symbology = "PostNet5" })), "symbology", "invalid");

    [Fact]
    public void Ean13_with_batch_conflicts_because_it_cannot_carry_it() =>
        AssertIssue(Check(Bottle(s => s with { Batch = "L42" })), "symbology", "conflict");

    [Fact]
    public void Case_with_gs1_128_encodes_gtin_date_count_and_batch_in_order()
    {
        var r = Check(Bottle(s => s with
        {
            PackagingLevel = "case", Symbology = "GS1-128", Gtin = Gtin14,
            BestBefore = "2027-03-31", ItemCount = 12, Batch = "LOT42",
        }));

        Assert.True(r.Ok);
        Assert.Equal($"(01){Gtin14}(15)270331(37)12(10)LOT42", r.Request!.Data);
    }

    [Fact]
    public void Gs1_128_pads_a_13_digit_gtin_to_14()
    {
        var r = Check(Bottle(s => s with { Symbology = "GS1-128" }));

        Assert.Equal($"(01)0{Gtin13}", r.Request!.Data);
    }

    [Fact]
    public void Gs1_128_rejects_a_gtin_without_check_digit() =>
        AssertIssue(Check(Bottle(s => s with { Symbology = "GS1-128", Gtin = Gtin13[..12] })), "gtin", "invalid");

    [Fact]
    public void Pallet_with_sscc_builds_gs1_128()
    {
        var r = Check(new LabelSpec
        {
            ProductName = "Palette Apfelsaft", PackagingLevel = "pallet", Symbology = "GS1-128", Sscc = Sscc(),
        });

        Assert.True(r.Ok);
        Assert.Equal($"(00){Sscc()}", r.Request!.Data);
    }

    [Fact]
    public void Pallet_needs_sscc_and_a_gs1_code()
    {
        var r = Check(Bottle(s => s with { PackagingLevel = "pallet" }));

        AssertIssue(r, "sscc", "missing");
        AssertIssue(r, "symbology", "conflict");
    }

    [Fact]
    public void Bad_sscc_check_digit_is_invalid()
    {
        var bad = Sscc()[..17] + (Sscc()[17] == '0' ? '1' : '0');
        var r = Check(new LabelSpec
        {
            ProductName = "P", PackagingLevel = "pallet", Symbology = "GS1-128", Sscc = bad,
        });

        AssertIssue(r, "sscc", "invalid");
    }

    [Fact]
    public void Ean14_on_consumer_unit_conflicts() =>
        AssertIssue(Check(Bottle(s => s with { Symbology = "EAN14", Gtin = Gtin14 })), "symbology", "conflict");

    [Fact]
    public void Item_count_on_consumer_unit_conflicts() =>
        AssertIssue(Check(Bottle(s => s with { Symbology = "GS1-128", ItemCount = 6 })), "itemCount", "conflict");

    [Theory]
    [InlineData("2026-09-28", "conflict")]   // yesterday
    [InlineData("2027-02-30", "invalid")]    // not a real date
    [InlineData("31.03.2027", "invalid")]    // wrong format
    public void Bad_best_before_dates_are_reported(string date, string kind) =>
        AssertIssue(Check(Bottle(s => s with { Symbology = "GS1-128", BestBefore = date })), "bestBefore", kind);

    [Fact]
    public void Batch_longer_than_20_characters_is_invalid() =>
        AssertIssue(Check(Bottle(s => s with { Symbology = "GS1-128", Batch = new string('A', 21) })), "batch", "invalid");

    [Fact]
    public void Digital_link_is_built_from_gtin_batch_and_date()
    {
        var r = Check(Bottle(s => s with
        {
            Symbology = "GS1DigitalLink_QRCode", Batch = "A/1", BestBefore = "2027-03-31",
        }));

        Assert.Equal($"https://id.gs1.org/01/0{Gtin13}/10/A%2F1?15=270331", r.Request!.Data);
    }

    [Fact]
    public void Plain_qr_needs_an_https_url()
    {
        var qr = Bottle(s => s with { Symbology = "QRCode" });

        AssertIssue(Check(qr), "url", "missing");
        AssertIssue(Check(qr with { Url = "http://example.com" }), "url", "invalid");
        Assert.Equal("https://example.com", Check(qr with { Url = "https://example.com" }).Request!.Data);
    }

    [Fact]
    public void Explicit_size_uses_fit_unit_so_the_symbol_is_scaled_not_cropped()
    {
        var sized = Check(Bottle(s => s with { WidthMm = 40, HeightMm = 20 })).Request!;
        Assert.Equal(("fit", 40, 20), (sized.Unit, sized.Width, sized.Height));
        Assert.Null(sized.ModuleWidth);

        AssertIssue(Check(Bottle(s => s with { WidthMm = 40 })), "heightMm", "missing");
    }
}

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

    [Theory]
    [InlineData("EAN13", "400638133393", "4006381333931")]
    [InlineData("EAN8", "9638507", "96385074")]
    [InlineData("UPCA", "03600029145", "036000291452")]
    [InlineData("EAN14", "1400638133393", "14006381333938")]
    public void Missing_check_digit_is_computed_and_returned_in_the_label(string symbology, string given, string full)
    {
        var level = symbology == "EAN14" ? "case" : "consumer_unit"; // EAN-14 is a trade-unit code
        var r = Check(Bottle(s => s with { Symbology = symbology, Gtin = given, PackagingLevel = level }));

        Assert.True(r.Ok);
        Assert.Equal(full, r.Request!.Data);
        Assert.Equal(full, r.Label.Gtin);
    }

    [Fact]
    public void Complete_gtin_is_left_unchanged() =>
        Assert.Equal(Gtin13, Check(Bottle()).Label.Gtin);

    [Fact]
    public void Past_best_before_date_is_accepted_once_the_user_confirmed_it()
    {
        var past = Bottle(s => s with { Symbology = "GS1-128", BestBefore = "2020-03-31" });

        AssertIssue(Check(past), "bestBefore", "conflict");
        var confirmed = Check(past with { AllowPastDate = true });
        Assert.True(confirmed.Ok);
        Assert.Contains("(15)200331", confirmed.Request!.Data);
    }

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

    // ---- EAN-8, UPC-A ----

    [Theory]
    [InlineData("EAN8", "96385074")]
    [InlineData("UPCA", "036000291452")]
    public void Complete_ean8_and_upca_pass_through(string symbology, string gtin)
    {
        var r = Check(Bottle(s => s with { Symbology = symbology, Gtin = gtin }));

        Assert.True(r.Ok);
        Assert.Equal(gtin, r.Request!.Data);
    }

    [Theory]
    [InlineData("EAN8", "96385075", "check digit")]     // wrong check digit
    [InlineData("EAN8", "123", "needs 7 or 8 digits")]
    [InlineData("UPCA", "036000291453", "check digit")]
    [InlineData("UPCA", "4006381333931", "needs 11 or 12 digits")] // an EAN-13 given as UPC-A
    public void Ean8_and_upca_reject_bad_gtins(string symbology, string gtin, string detailPart)
    {
        var r = Check(Bottle(s => s with { Symbology = symbology, Gtin = gtin }));

        AssertIssue(r, "gtin", "invalid");
        Assert.Contains(detailPart, r.Issues.Single().Detail);
    }

    // ---- Code 128 / Code 39 ----

    [Theory]
    [InlineData("Code128")]
    [InlineData("Code39")]
    public void Code128_and_code39_encode_the_plain_gtin(string symbology)
    {
        var r = Check(Bottle(s => s with { Symbology = symbology }));

        Assert.True(r.Ok);
        Assert.Equal(Gtin13, r.Request!.Data);
        Assert.Equal(0.25, r.Request.ModuleWidth);
    }

    [Theory]
    [InlineData("Code128")]
    [InlineData("Code39")]
    public void Code128_and_code39_need_digits_and_cannot_carry_batch(string symbology)
    {
        AssertIssue(Check(Bottle(s => s with { Symbology = symbology, Gtin = "40063A" })), "gtin", "invalid");
        AssertIssue(Check(Bottle(s => s with { Symbology = symbology, Gtin = null })), "gtin", "missing");
        AssertIssue(Check(Bottle(s => s with { Symbology = symbology, Batch = "L1" })), "symbology", "conflict");
    }

    // ---- plain QR / DataMatrix ----

    [Theory]
    [InlineData("QRCode")]
    [InlineData("DataMatrix")]
    public void Plain_2d_codes_encode_the_url_and_reject_gs1_attributes(string symbology)
    {
        var withUrl = Bottle(s => s with { Symbology = symbology, Url = "https://example.com/p/1" });

        var ok = Check(withUrl);
        Assert.True(ok.Ok);
        Assert.Equal(("https://example.com/p/1", 0.5), (ok.Request!.Data, ok.Request.ModuleWidth));
        AssertIssue(Check(withUrl with { Batch = "L1" }), "symbology", "conflict");
    }

    // ---- GS1 2D element strings and pallets ----

    [Theory]
    [InlineData("GS1DataMatrix")]
    [InlineData("GS1QRCode")]
    public void Gs1_2d_codes_use_the_same_element_string_as_gs1_128(string symbology)
    {
        var r = Check(Bottle(s => s with { Symbology = symbology, Batch = "L1" }));

        Assert.Equal($"(01)0{Gtin13}(10)L1", r.Request!.Data);
        Assert.Equal(0.5, r.Request.ModuleWidth);
    }

    [Fact]
    public void Pallet_can_add_gtin_and_batch_to_the_sscc()
    {
        var r = Check(new LabelSpec
        {
            ProductName = "P", PackagingLevel = "pallet", Symbology = "GS1DataMatrix", Sscc = Sscc(), Gtin = Gtin14, Batch = "L1",
        });

        Assert.Equal($"(00){Sscc()}(01){Gtin14}(10)L1", r.Request!.Data);
    }

    [Fact]
    public void Sscc_needs_18_digits()
    {
        var r = Check(new LabelSpec
        {
            ProductName = "P", PackagingLevel = "pallet", Symbology = "GS1-128", Sscc = "12345",
        });

        AssertIssue(r, "sscc", "invalid");
    }

    [Fact]
    public void Item_count_conflicts_with_digital_link_and_sscc_with_non_pallets()
    {
        var caseLabel = Bottle(s => s with { PackagingLevel = "case", Symbology = "GS1DigitalLink_QRCode", Gtin = Gtin14, ItemCount = 12 });

        AssertIssue(Check(caseLabel), "itemCount", "conflict");
        AssertIssue(Check(Bottle(s => s with { Sscc = Sscc() })), "sscc", "conflict");
    }
}

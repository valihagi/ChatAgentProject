using ChatAgent.Api.Barcode;

namespace ChatAgent.Tests;

public class BarcodeRequestTests
{
    [Fact]
    public void ToForm_contains_required_fields_and_omits_unset_options()
    {
        var form = new BarcodeRequest("EAN13", "4006381333931").ToForm();

        Assert.Equal("EAN13", form["code"]);
        Assert.Equal("4006381333931", form["data"]);
        Assert.Equal("png", form["imagetype"]);
        Assert.False(form.ContainsKey("dpi"));
        Assert.False(form.ContainsKey("accessid"));
    }

    [Fact]
    public void ToForm_maps_options_using_invariant_culture()
    {
        var form = new BarcodeRequest("EAN13", "4006381333931")
        {
            Dpi = 300, Unit = "mm", Width = 37.5, ShowHrt = false, QuietZone = 2.5,
        }.ToForm();

        Assert.Equal("300", form["dpi"]);
        Assert.Equal("mm", form["unit"]);
        Assert.Equal("37.5", form["width"]);
        Assert.Equal("0", form["showhrt"]);
        Assert.Equal("2.5", form["quiet"]);
    }

    [Theory]
    [InlineData("Nope", "123")]
    [InlineData("EAN13", " ")]
    public void ToForm_rejects_unsupported_type_or_empty_data(string code, string data) =>
        Assert.Throws<ArgumentException>(() => new BarcodeRequest(code, data).ToForm());
}

using System.Globalization;

namespace ChatAgent.Api.Barcode;

/// <summary>Parameters of one TEC-IT barcode.ashx call (see API reference, section 1). Unset options use API defaults.</summary>
public record BarcodeRequest(string Code, string Data)
{
    public string Format { get; init; } = "png";   // png | jpg | gif (svg is subscriber-only)
    public int? Dpi { get; init; }                 // 72..300 for non-subscribers
    public string? Unit { get; init; }             // fit (default: width/height in mm, symbol is scaled into the box) | mm (crops!) | mils | px | min
    public double? ModuleWidth { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public int? Rotation { get; init; }            // 0 | 90 | 180 | 270
    public string? Color { get; init; }            // RRGGBB
    public string? BgColor { get; init; }          // RRGGBB
    public double? QuietZone { get; init; }
    public string? QuietUnit { get; init; }        // px | mm | mils
    public string? EcLevel { get; init; }          // QR: L | M | Q | H
    public bool? ShowHrt { get; init; }            // human-readable text
    public string? Hrt { get; init; }              // custom human-readable text
    public string? TextPosition { get; init; }     // above
    public string? TextAlign { get; init; }        // left | center | right
    public string? Font { get; init; }             // "Arial,12,bold"
    public string? TextColor { get; init; }        // RRGGBB

    public Dictionary<string, string> ToForm()
    {
        if (!BarcodeTypes.Allowed.Contains(Code))
            throw new ArgumentException($"Barcode type '{Code}' is not supported.", nameof(Code));
        if (string.IsNullOrWhiteSpace(Data))
            throw new ArgumentException("Barcode data must not be empty.", nameof(Data));

        var form = new Dictionary<string, string>
        {
            ["code"] = Code,
            ["data"] = Data,
            ["imagetype"] = Format,
        };

        void Add(string key, object? value)
        {
            if (value is not null) form[key] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }

        Add("dpi", Dpi);
        Add("unit", Unit);
        Add("modulewidth", ModuleWidth);
        Add("width", Width);
        Add("height", Height);
        Add("rotation", Rotation);
        Add("color", Color);
        Add("bgcolor", BgColor);
        Add("quiet", QuietZone);
        Add("qunit", QuietUnit);
        Add("eclevel", EcLevel);
        Add("showhrt", ShowHrt is null ? null : ShowHrt.Value ? "1" : "0");
        Add("hrt", Hrt);
        Add("textposition", TextPosition);
        Add("textalign", TextAlign);
        Add("font", Font);
        Add("textcolor", TextColor);
        return form;
    }
}

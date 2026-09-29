using System.Globalization;

namespace ChatAgent.Api.Barcode;

/// <summary>
/// The subset of TEC-IT barcode.ashx parameters (API reference, section 1) this app uses. Unset options use API defaults.
/// Further options (colors, rotation, quiet zone, human-readable text, QR error correction) can be added as init properties
/// plus one line in <see cref="ToForm"/>.
/// </summary>
public record BarcodeRequest(string Code, string Data)
{
    public string Format { get; init; } = "png";   // png | jpg | gif (svg is subscriber-only)
    public int? Dpi { get; init; }                 // 72..300 for non-subscribers
    public string? Unit { get; init; }             // fit: width/height in mm, symbol is scaled into the box | mm: sizes in mm, CROPS the symbol
    public double? ModuleWidth { get; init; }      // in Unit; fixes the physical size
    public double? Width { get; init; }
    public double? Height { get; init; }

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
        return form;
    }
}

using System.Text.RegularExpressions;
using ChatAgent.Api.Barcode;

namespace ChatAgent.Api.Agent;

/// <param name="Label">The specification with derived values filled in (e.g. a computed GTIN check digit).</param>
public record ValidationResult(List<AgentIssue> Issues, BarcodeRequest? Request, LabelSpec Label)
{
    public bool Ok => Issues.Count == 0 && Request is not null;
}

/// <summary>
/// Deterministic checks on the LLM's specification (lengths, check digits, dates, symbology/packaging
/// fit) and construction of the barcode data string. The LLM never builds barcode data itself.
/// </summary>
public static partial class LabelValidator
{
    public const int Dpi = 300; // maximum for non-subscribers

    private static readonly string[] Levels = ["consumer_unit", "case", "pallet"];

    // EAN/UPC family -> (full GTIN length, accepted input lengths; a missing check digit is computed here)
    private static readonly Dictionary<string, (int Full, int[] Lengths)> Linear = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EAN13"] = (13, [12, 13]), ["EAN8"] = (8, [7, 8]), ["UPCA"] = (12, [11, 12]), ["EAN14"] = (14, [13, 14]),
    };

    private static readonly HashSet<string> Gs1Element = new(StringComparer.OrdinalIgnoreCase)
        { "GS1-128", "GS1QRCode", "GS1DataMatrix" };

    private static readonly HashSet<string> DigitalLink = new(StringComparer.OrdinalIgnoreCase)
        { "GS1DigitalLink_QRCode", "GS1DigitalLink_DataMatrix" };

    [GeneratedRegex(@"^[A-Za-z0-9\-._/+]{1,20}$")]
    private static partial Regex BatchPattern();

    /// <summary>Collects findings; the helpers return null so a failed check can end a data builder in one line.</summary>
    private sealed class Findings
    {
        public List<AgentIssue> Items { get; } = [];

        public void Add(string field, string kind, string detail) => Items.Add(new(field, kind, detail));

        public string? Missing(string field)
        {
            Add(field, "missing", $"{field} is required for this label.");
            return null;
        }

        public string? Invalid(string field, string detail)
        {
            Add(field, "invalid", detail);
            return null;
        }
    }

    public static ValidationResult Validate(LabelSpec s, DateOnly today)
    {
        var findings = new Findings();

        CheckRequiredFields(s, findings);
        var yymmdd = CheckBestBefore(s, today, findings);
        CheckOptionalFields(s, findings);
        var symbology = ResolveSymbology(s, findings);
        if (symbology is not null) CheckPackagingFit(s, symbology, findings);

        var data = symbology is null ? null : BuildData(symbology, s, yymmdd, findings);
        if (findings.Items.Count > 0 || symbology is null || data is null) return new(findings.Items, null, s);

        // For EAN/UPC codes `data` is the GTIN including a check digit we may have computed.
        return new(findings.Items, BuildRequest(symbology, data, s), Linear.ContainsKey(symbology) ? s with { Gtin = data } : s);
    }

    /// <summary>
    /// Bar/module width in mm when the user gave no size. Measured with the API at 300 DPI: EAN-13 at 0.33 mm is
    /// 37.3 mm wide (GS1 nominal 37.29 mm); without this the API picks a much larger scale (a long GS1-128 was 240 mm).
    /// </summary>
    public static double ModuleWidthMm(string symbology)
    {
        if (Linear.ContainsKey(symbology) && !symbology.Equals("EAN14", StringComparison.OrdinalIgnoreCase)) return 0.33; // EAN-13/8, UPC-A nominal
        if (symbology.Contains("QR", StringComparison.OrdinalIgnoreCase) || symbology.Contains("DataMatrix", StringComparison.OrdinalIgnoreCase)) return 0.5; // 2D
        return 0.25; // GS1-128, EAN-14, Code 128/39: GS1 minimum X-dimension, keeps long strings printable
    }

    private static BarcodeRequest BuildRequest(string symbology, string data, LabelSpec s)
    {
        var request = new BarcodeRequest(symbology, data) { Dpi = Dpi };
        return s.WidthMm is { } w && s.HeightMm is { } h
            ? request with { Unit = "fit", Width = w, Height = h }                // scales the symbol into the box (unit=mm would crop it)
            : request with { Unit = "mm", ModuleWidth = ModuleWidthMm(symbology) }; // deterministic physical size
    }

    // ---- field checks ----------------------------------------------------------------------------

    private static void CheckRequiredFields(LabelSpec s, Findings f)
    {
        if (string.IsNullOrWhiteSpace(s.ProductName)) f.Add("productName", "missing", "Product name is required.");

        if (s.PackagingLevel is null) f.Add("packagingLevel", "missing", "Packaging level (consumer_unit, case, pallet) is required.");
        else if (!Levels.Contains(s.PackagingLevel)) f.Add("packagingLevel", "invalid", $"Unknown packaging level '{s.PackagingLevel}'.");

        if (s.PackagingLevel == "pallet" && s.Sscc is null) f.Add("sscc", "missing", "Pallet labels need an SSCC (18 digits).");
    }

    /// <summary>Returns the date as GS1 YYMMDD, or null if absent or rejected.</summary>
    private static string? CheckBestBefore(LabelSpec s, DateOnly today, Findings f)
    {
        if (s.BestBefore is null) return null;

        if (!DateOnly.TryParseExact(s.BestBefore, "yyyy-MM-dd", out var date))
            f.Add("bestBefore", "invalid", $"'{s.BestBefore}' is not a valid date (expected yyyy-MM-dd).");
        else if (date < today && s.AllowPastDate != true)
            f.Add("bestBefore", "conflict", $"Best-before date {s.BestBefore} is in the past. Ask the user to confirm it is intended.");
        else
            return date.ToString("yyMMdd");
        return null;
    }

    private static void CheckOptionalFields(LabelSpec s, Findings f)
    {
        if (s.Batch is not null && !BatchPattern().IsMatch(s.Batch))
            f.Add("batch", "invalid", "Batch must be 1-20 characters (letters, digits, - . _ / +).");

        if (s.ItemCount is < 1 or > 99999999) f.Add("itemCount", "invalid", "Item count must be between 1 and 99999999.");
        if (s.ItemCount is not null && s.PackagingLevel is not (null or "case"))
            f.Add("itemCount", "conflict", "Item count only applies to case labels.");
        if (s.Sscc is not null && s.PackagingLevel is not (null or "pallet"))
            f.Add("sscc", "conflict", "SSCC only applies to pallet labels.");

        if ((s.WidthMm is null) != (s.HeightMm is null))
            f.Add(s.WidthMm is null ? "widthMm" : "heightMm", "missing", "Give width and height together (mm).");
        else if (s.WidthMm is <= 0 or > 300 || s.HeightMm is <= 0 or > 300)
            f.Add("widthMm", "invalid", "Width and height must be between 0 and 300 mm.");
    }

    private static string? ResolveSymbology(LabelSpec s, Findings f)
    {
        if (s.Symbology is null) return f.Missing("symbology");
        if (BarcodeTypes.Allowed.TryGetValue(s.Symbology, out var canonical)) return canonical;
        return f.Invalid("symbology", $"Barcode type '{s.Symbology}' is not supported.");
    }

    private static void CheckPackagingFit(LabelSpec s, string symbology, Findings f)
    {
        if (s.PackagingLevel == "pallet" && !Gs1Element.Contains(symbology))
            f.Add("symbology", "conflict", "Pallet labels need a GS1 element-string code (GS1-128, GS1DataMatrix or GS1QRCode) carrying the SSCC.");
        if (s.PackagingLevel == "consumer_unit" && symbology.Equals("EAN14", StringComparison.OrdinalIgnoreCase))
            f.Add("symbology", "conflict", "EAN14 is for trade units (cases); use EAN13 for consumer units.");
    }

    // ---- barcode data per symbology family -------------------------------------------------------

    private static string? BuildData(string symbology, LabelSpec s, string? yymmdd, Findings f)
    {
        if (Linear.TryGetValue(symbology, out var rule)) return LinearData(symbology, rule, s, f);
        if (Gs1Element.Contains(symbology)) return Gs1ElementString(s, yymmdd, f);
        if (DigitalLink.Contains(symbology)) return DigitalLinkUrl(s, yymmdd, f);
        if (symbology.StartsWith("Code", StringComparison.OrdinalIgnoreCase)) return PlainGtin(symbology, s, f); // Code128 / Code39
        return UrlContent(symbology, s, f);                                                                    // QRCode / DataMatrix
    }

    private static bool HasGs1Attributes(LabelSpec s) => s.Batch is not null || s.BestBefore is not null || s.ItemCount is not null;

    /// <summary>For codes that only carry a plain value: reports a conflict if batch, date or count were requested.</summary>
    private static bool CarriesNoGs1Attributes(string symbology, LabelSpec s, Findings f)
    {
        if (!HasGs1Attributes(s)) return true;
        f.Add("symbology", "conflict", $"{symbology} cannot carry batch, best-before date or item count. Use GS1-128 or drop those values.");
        return false;
    }

    /// <summary>EAN-13/8, UPC-A, EAN-14: the GTIN itself, with a computed check digit if it was omitted.</summary>
    private static string? LinearData(string symbology, (int Full, int[] Lengths) rule, LabelSpec s, Findings f)
    {
        if (s.Gtin is null) return f.Missing("gtin");
        if (!Gs1.IsDigits(s.Gtin) || !rule.Lengths.Contains(s.Gtin.Length))
            return f.Invalid("gtin", $"{symbology} needs {string.Join(" or ", rule.Lengths)} digits, got '{s.Gtin}'.");
        if (s.Gtin.Length == rule.Full && !Gs1.HasValidCheckDigit(s.Gtin))
            return f.Invalid("gtin", $"'{s.Gtin}' has a wrong check digit (expected {Gs1.CheckDigit(s.Gtin[..^1])}).");

        var full = s.Gtin.Length == rule.Full ? s.Gtin : s.Gtin + Gs1.CheckDigit(s.Gtin);
        return CarriesNoGs1Attributes(symbology, s, f) ? full : null;
    }

    /// <summary>GS1-128 / GS1 DataMatrix / GS1 QR: element string with application identifiers.</summary>
    private static string Gs1ElementString(LabelSpec s, string? yymmdd, Findings f)
    {
        var isPallet = s.PackagingLevel == "pallet";
        var parts = new List<string>();

        if (isPallet && s.Sscc is not null) // a missing SSCC is already reported in CheckRequiredFields
        {
            if (s.Sscc.Length != 18 || !Gs1.IsDigits(s.Sscc)) f.Invalid("sscc", "SSCC must be exactly 18 digits.");
            else if (!Gs1.HasValidCheckDigit(s.Sscc)) f.Invalid("sscc", $"SSCC has a wrong check digit (expected {Gs1.CheckDigit(s.Sscc[..^1])}).");
            else parts.Add($"(00){s.Sscc}");
        }
        if (Gtin14(s.Gtin, required: !isPallet, f) is { } gtin) parts.Add($"(01){gtin}");
        if (yymmdd is not null) parts.Add($"(15){yymmdd}");
        if (s.ItemCount is { } n) parts.Add($"(37){n}");
        if (s.Batch is not null) parts.Add($"(10){s.Batch}"); // variable length goes last
        return string.Concat(parts);
    }

    /// <summary>GS1 Digital Link on GS1's resolver: /01/{gtin14}[/10/{batch}][?15={yymmdd}].</summary>
    private static string? DigitalLinkUrl(LabelSpec s, string? yymmdd, Findings f)
    {
        if (s.ItemCount is not null) f.Add("itemCount", "conflict", "A GS1 Digital Link cannot carry an item count here; use GS1-128.");
        if (Gtin14(s.Gtin, required: true, f) is not { } gtin) return null;

        var link = $"https://id.gs1.org/01/{gtin}";
        if (s.Batch is not null) link += $"/10/{Uri.EscapeDataString(s.Batch)}";
        if (yymmdd is not null) link += $"?15={yymmdd}";
        return link;
    }

    /// <summary>Code 128 / Code 39: the GTIN as plain digits.</summary>
    private static string? PlainGtin(string symbology, LabelSpec s, Findings f)
    {
        if (s.Gtin is null) return f.Missing("gtin");
        if (!Gs1.IsDigits(s.Gtin)) return f.Invalid("gtin", "GTIN must contain digits only.");
        return CarriesNoGs1Attributes(symbology, s, f) ? s.Gtin : null;
    }

    /// <summary>Plain QR / DataMatrix: we only encode an https URL.</summary>
    private static string? UrlContent(string symbology, LabelSpec s, Findings f)
    {
        if (s.Url is null) return f.Missing("url");
        if (!Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return f.Invalid("url", "URL must be an absolute https:// address.");
        return CarriesNoGs1Attributes(symbology, s, f) ? s.Url : null;
    }

    /// <summary>Validates a GTIN for GS1 use (full number incl. check digit) and returns it as GTIN-14.</summary>
    private static string? Gtin14(string? gtin, bool required, Findings f)
    {
        if (gtin is null) return required ? f.Missing("gtin") : null;
        if (!Gs1.IsDigits(gtin) || gtin.Length is not (8 or 12 or 13 or 14))
            return f.Invalid("gtin", $"GTIN must be 8, 12, 13 or 14 digits, got '{gtin}'.");
        if (!Gs1.HasValidCheckDigit(gtin))
            return f.Invalid("gtin", $"'{gtin}' has a wrong check digit (expected {Gs1.CheckDigit(gtin[..^1])}).");
        return gtin.PadLeft(14, '0');
    }
}

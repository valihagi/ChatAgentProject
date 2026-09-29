using System.Text.RegularExpressions;
using ChatAgent.Api.Barcode;

namespace ChatAgent.Api.Agent;

public record ValidationResult(List<AgentIssue> Issues, BarcodeRequest? Request)
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

    // symbology -> (full GTIN length, accepted input lengths; the API adds the check digit if omitted)
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

    public static ValidationResult Validate(LabelSpec s, DateOnly today)
    {
        var issues = new List<AgentIssue>();
        void Add(string field, string kind, string detail) => issues.Add(new(field, kind, detail));

        if (string.IsNullOrWhiteSpace(s.ProductName)) Add("productName", "missing", "Product name is required.");

        if (s.PackagingLevel is null) Add("packagingLevel", "missing", "Packaging level (consumer_unit, case, pallet) is required.");
        else if (!Levels.Contains(s.PackagingLevel)) Add("packagingLevel", "invalid", $"Unknown packaging level '{s.PackagingLevel}'.");

        string? symbology = null;
        if (s.Symbology is null) Add("symbology", "missing", "Barcode type is required.");
        else if (!BarcodeTypes.Allowed.TryGetValue(s.Symbology, out symbology))
            Add("symbology", "invalid", $"Barcode type '{s.Symbology}' is not supported.");

        // Optional GS1 attributes.
        string? yymmdd = null;
        if (s.Batch is not null && !BatchPattern().IsMatch(s.Batch))
            Add("batch", "invalid", "Batch must be 1-20 characters (letters, digits, - . _ / +).");
        if (s.BestBefore is not null)
        {
            if (!DateOnly.TryParseExact(s.BestBefore, "yyyy-MM-dd", out var date))
                Add("bestBefore", "invalid", $"'{s.BestBefore}' is not a valid date (expected yyyy-MM-dd).");
            else if (date < today)
                Add("bestBefore", "conflict", $"Best-before date {s.BestBefore} is in the past.");
            else yymmdd = date.ToString("yyMMdd");
        }
        if (s.ItemCount is < 1 or > 99999999) Add("itemCount", "invalid", "Item count must be between 1 and 99999999.");
        if (s.ItemCount is not null && s.PackagingLevel is not (null or "case"))
            Add("itemCount", "conflict", "Item count only applies to case labels.");
        if (s.Sscc is not null && s.PackagingLevel is not (null or "pallet"))
            Add("sscc", "conflict", "SSCC only applies to pallet labels.");
        if ((s.WidthMm is null) != (s.HeightMm is null))
            Add(s.WidthMm is null ? "widthMm" : "heightMm", "missing", "Give width and height together (mm).");
        else if (s.WidthMm is <= 0 or > 300 || s.HeightMm is <= 0 or > 300)
            Add("widthMm", "invalid", "Width and height must be between 0 and 300 mm.");

        if (s.PackagingLevel == "pallet" && s.Sscc is null) Add("sscc", "missing", "Pallet labels need an SSCC (18 digits).");

        var hasAttributes = s.Batch is not null || s.BestBefore is not null || s.ItemCount is not null;

        // Packaging level vs. symbology.
        if (symbology is not null)
        {
            if (s.PackagingLevel == "pallet" && !Gs1Element.Contains(symbology))
                Add("symbology", "conflict", "Pallet labels need a GS1 element-string code (GS1-128, GS1DataMatrix or GS1QRCode) carrying the SSCC.");
            if (s.PackagingLevel == "consumer_unit" && symbology.Equals("EAN14", StringComparison.OrdinalIgnoreCase))
                Add("symbology", "conflict", "EAN14 is for trade units (cases); use EAN13 for consumer units.");
        }

        var data = symbology is null ? null : BuildData(symbology, s, yymmdd, hasAttributes, Add);
        if (issues.Count > 0 || symbology is null || data is null) return new(issues, null);

        var request = new BarcodeRequest(symbology, data) { Dpi = Dpi };
        if (s.WidthMm is { } w && s.HeightMm is { } h)
            request = request with { Unit = "mm", Width = w, Height = h };
        return new(issues, request);
    }

    private static string? BuildData(string symbology, LabelSpec s, string? yymmdd, bool hasAttributes,
        Action<string, string, string> add)
    {
        if (Linear.TryGetValue(symbology, out var rule))
        {
            if (s.Gtin is null) return Missing("gtin", add);
            if (!Gs1.IsDigits(s.Gtin) || !rule.Lengths.Contains(s.Gtin.Length))
                return Fail("gtin", $"{symbology} needs {string.Join(" or ", rule.Lengths)} digits, got '{s.Gtin}'.", add);
            if (s.Gtin.Length == rule.Full && !Gs1.HasValidCheckDigit(s.Gtin))
                return Fail("gtin", $"'{s.Gtin}' has a wrong check digit (expected {Gs1.CheckDigit(s.Gtin[..^1])}).", add);
            return NoAttributes(symbology, hasAttributes, add) ? s.Gtin : null;
        }

        if (Gs1Element.Contains(symbology))
        {
            var isPallet = s.PackagingLevel == "pallet";
            var parts = new List<string>();
            if (isPallet)
            {
                if (s.Sscc is not null) // a missing SSCC is already reported in Validate
                {
                    if (s.Sscc.Length != 18 || !Gs1.IsDigits(s.Sscc)) Fail("sscc", "SSCC must be exactly 18 digits.", add);
                    else if (!Gs1.HasValidCheckDigit(s.Sscc)) Fail("sscc", $"SSCC has a wrong check digit (expected {Gs1.CheckDigit(s.Sscc[..^1])}).", add);
                    else parts.Add($"(00){s.Sscc}");
                }
            }
            if (Gtin14(s.Gtin, required: !isPallet, add) is { } gtin) parts.Add($"(01){gtin}");
            if (yymmdd is not null) parts.Add($"(15){yymmdd}");
            if (s.ItemCount is { } n) parts.Add($"(37){n}");
            if (s.Batch is not null) parts.Add($"(10){s.Batch}"); // variable length goes last
            return string.Concat(parts);
        }

        if (DigitalLink.Contains(symbology))
        {
            if (s.ItemCount is not null) add("itemCount", "conflict", "A GS1 Digital Link cannot carry an item count here; use GS1-128.");
            if (Gtin14(s.Gtin, required: true, add) is not { } gtin) return null;
            var link = $"https://id.gs1.org/01/{gtin}";
            if (s.Batch is not null) link += $"/10/{Uri.EscapeDataString(s.Batch)}";
            if (yymmdd is not null) link += $"?15={yymmdd}";
            return link;
        }

        if (symbology.StartsWith("Code", StringComparison.OrdinalIgnoreCase)) // Code128 / Code39
        {
            if (s.Gtin is null) return Missing("gtin", add);
            if (!Gs1.IsDigits(s.Gtin)) return Fail("gtin", "GTIN must contain digits only.", add);
            return NoAttributes(symbology, hasAttributes, add) ? s.Gtin : null;
        }

        // QRCode / DataMatrix: plain content, we only support a URL.
        if (s.Url is null) return Missing("url", add);
        if (!Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Fail("url", "URL must be an absolute https:// address.", add);
        return NoAttributes(symbology, hasAttributes, add) ? s.Url : null;
    }

    /// <summary>Validates a GTIN for GS1 use (full number incl. check digit) and returns it as GTIN-14.</summary>
    private static string? Gtin14(string? gtin, bool required, Action<string, string, string> add)
    {
        if (gtin is null) return required ? Missing("gtin", add) : null;
        if (!Gs1.IsDigits(gtin) || gtin.Length is not (8 or 12 or 13 or 14))
            return Fail("gtin", $"GTIN must be 8, 12, 13 or 14 digits, got '{gtin}'.", add);
        if (!Gs1.HasValidCheckDigit(gtin))
            return Fail("gtin", $"'{gtin}' has a wrong check digit (expected {Gs1.CheckDigit(gtin[..^1])}).", add);
        return gtin.PadLeft(14, '0');
    }

    private static bool NoAttributes(string symbology, bool hasAttributes, Action<string, string, string> add)
    {
        if (!hasAttributes) return true;
        add("symbology", "conflict", $"{symbology} cannot carry batch, best-before date or item count. Use GS1-128 or drop those values.");
        return false;
    }

    private static string? Missing(string field, Action<string, string, string> add)
    {
        add(field, "missing", $"{field} is required for this label.");
        return null;
    }

    private static string? Fail(string field, string detail, Action<string, string, string> add)
    {
        add(field, "invalid", detail);
        return null;
    }
}

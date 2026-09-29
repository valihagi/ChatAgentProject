using System.Globalization;
using System.Text.RegularExpressions;

namespace ChatAgent.Api.Agent;

/// <summary>Parsing and canonical formatting of a printed net volume such as "0,75 l", "75cl" or "330 ML".</summary>
public static partial class NetVolume
{
    public const double MaxMilliliters = 100_000; // 100 l: anything above is not a beverage label we can trust

    [GeneratedRegex(@"^(?<num>\d+(?:[.,](?<frac>\d+))?)\s*(?<unit>ml|cl|l)$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>Returns the canonical text ("0,75 l"), or the input unchanged if it is not a valid volume.</summary>
    public static string Normalize(string? text) => text is null ? "" : TryParse(text, out _, out var canonical) ? canonical : text;

    /// <param name="error">Why the text is not acceptable, when it is not.</param>
    public static bool TryParse(string text, out double milliliters, out string canonical, out string? error)
    {
        milliliters = 0;
        canonical = text;
        error = null;

        var match = Pattern().Match(text.Trim());
        if (!match.Success)
        {
            error = "Net volume needs a number and a unit ml, cl or l (e.g. 0,75 l).";
            return false;
        }

        // "1.000 ml" means 1000 ml in German and 1 ml in English: refuse to guess.
        if (match.Groups["frac"].Value.Length == 3)
        {
            error = $"'{text.Trim()}' is ambiguous (thousands separator or decimals?). Please write it without separator, e.g. 1000 ml.";
            return false;
        }

        var number = double.Parse(match.Groups["num"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        milliliters = unit switch { "ml" => number, "cl" => number * 10, _ => number * 1000 };

        if (milliliters <= 0 || milliliters > MaxMilliliters)
        {
            error = $"Net volume '{text.Trim()}' is not plausible for a beverage.";
            return false;
        }

        canonical = $"{match.Groups["num"].Value} {unit}";
        return true;
    }

    public static bool TryParse(string text, out double milliliters, out string canonical) =>
        TryParse(text, out milliliters, out canonical, out _);
}

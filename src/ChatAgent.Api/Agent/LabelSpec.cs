using ChatAgent.Api.Barcode;

namespace ChatAgent.Api.Agent;

/// <summary>Everything the agent knows about the requested label. Null = unknown.</summary>
public record LabelSpec
{
    public string? ProductName { get; init; }
    public string? NetVolume { get; init; }
    public string? PackagingLevel { get; init; }   // consumer_unit | case | pallet
    public string? Symbology { get; init; }
    public string? Gtin { get; init; }
    public string? Batch { get; init; }
    public string? BestBefore { get; init; }       // yyyy-MM-dd
    public bool? AllowPastDate { get; init; }      // user explicitly confirmed a past best-before date
    public int? ItemCount { get; init; }
    public string? Sscc { get; init; }
    public string? Url { get; init; }
    public double? WidthMm { get; init; }
    public double? HeightMm { get; init; }

    /// <summary>Cleans up LLM output: trims, empty strings become null, canonical casing, no spaces in numbers.</summary>
    public LabelSpec Normalized() => this with
    {
        ProductName = Clean(ProductName),
        NetVolume = Clean(NetVolume),
        PackagingLevel = Clean(PackagingLevel)?.ToLowerInvariant().Replace('-', '_').Replace(' ', '_'),
        Symbology = Clean(Symbology) is { } sym && BarcodeTypes.Allowed.TryGetValue(sym, out var canonical) ? canonical : Clean(Symbology),
        Gtin = Number(Gtin),
        Sscc = Number(Sscc),
        Batch = Clean(Batch),
        BestBefore = Clean(BestBefore),
        Url = Clean(Url),
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Number(string? s)
    {
        var digits = Clean(s) is { } c ? new string(c.Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray()) : "";
        return digits.Length == 0 ? null : digits;
    }
}

public record AgentIssue(string Field, string Kind, string Detail);

/// <summary>The JSON object the LLM returns each turn (see Prompts/system-prompt.md).</summary>
public record AgentReply
{
    public string Message { get; init; } = "";
    public string Status { get; init; } = "needs_info";
    public List<AgentIssue> Issues { get; init; } = [];
    public LabelSpec Label { get; init; } = new();
}

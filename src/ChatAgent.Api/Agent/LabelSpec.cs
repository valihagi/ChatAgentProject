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
    public int? ItemCount { get; init; }
    public string? Sscc { get; init; }
    public string? Url { get; init; }
    public double? WidthMm { get; init; }
    public double? HeightMm { get; init; }
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

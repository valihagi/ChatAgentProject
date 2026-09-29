using System.Text.Json;
using System.Text.RegularExpressions;
using ChatAgent.Api.Agent;

namespace ChatAgent.Api.Chat;

/// <summary>
/// Offline stand-in for the LLM so development does not burn API quota. Speaks the same JSON protocol:
/// asks for a GTIN until one (12-14 digits) appears in the conversation, then reports "ready".
/// </summary>
public partial class MockChatModel : IChatModel
{
    [GeneratedRegex(@"\b\d{12,14}\b")]
    private static partial Regex GtinPattern();

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var last = history[^1].Text;
        AgentReply reply;

        if (last.StartsWith("[backend validation]"))
        {
            reply = new() { Message = "[mock] The backend found problems with that specification. Please correct the GTIN." };
        }
        else if (history.Where(m => m.Role == "user").Select(m => GtinPattern().Match(m.Text)).LastOrDefault(m => m.Success)
                 is { } match)
        {
            var isCase = match.Value.Length == 14;
            reply = new()
            {
                Message = $"[mock] Label for GTIN {match.Value} is ready.",
                Status = "ready",
                Label = new()
                {
                    ProductName = "Mock product",
                    NetVolume = "0,5 l",
                    PackagingLevel = isCase ? "case" : "consumer_unit",
                    Symbology = isCase ? "EAN14" : "EAN13",
                    Gtin = match.Value,
                },
            };
        }
        else
        {
            reply = new()
            {
                Message = "[mock] Please give me the product's GTIN (12-14 digits).",
                Issues = [new("gtin", "missing", "GTIN is required.")],
                Label = new() { ProductName = "Mock product" },
            };
        }

        return Task.FromResult(JsonSerializer.Serialize(reply, JsonSerializerOptions.Web));
    }
}

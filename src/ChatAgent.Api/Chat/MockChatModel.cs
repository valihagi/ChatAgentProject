namespace ChatAgent.Api.Chat;

/// <summary>Offline stand-in for the LLM so development does not burn API quota.</summary>
public class MockChatModel : IChatModel
{
    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var last = history.LastOrDefault(m => m.Role == "user")?.Text ?? "";
        return Task.FromResult($"[mock] You said: {last}");
    }
}

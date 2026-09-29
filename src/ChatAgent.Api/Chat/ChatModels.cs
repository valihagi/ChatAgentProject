namespace ChatAgent.Api.Chat;

public record ChatMessage(string Role, string Text);

public record ChatRequest(List<ChatMessage> Messages);

public record ChatResponse(string Reply);

public interface IChatModel
{
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct);
}

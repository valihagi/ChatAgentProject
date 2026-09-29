using ChatAgent.Api.Agent;

namespace ChatAgent.Api.Chat;

public record ChatMessage(string Role, string Text);

/// <summary>The server is stateless: the browser sends the conversation plus the last known label spec.</summary>
public record ChatRequest(List<ChatMessage> Messages, LabelSpec? Label = null);

/// <param name="Status">"needs_info" or "ready"</param>
/// <param name="Image">Data URL of the generated label barcode, only when ready.</param>
/// <param name="Dpi">Resolution of <paramref name="Image"/>, needed to print it at its physical size.</param>
public record ChatResponse(string Reply, string Status, LabelSpec Label, string? Image, int Dpi = LabelValidator.Dpi);

public interface IChatModel
{
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct);
}

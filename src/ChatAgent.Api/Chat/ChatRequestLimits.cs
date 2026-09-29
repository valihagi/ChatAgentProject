using System.Text.Json;

namespace ChatAgent.Api.Chat;

/// <summary>Cheap guard so anonymous callers cannot send unbounded text to the paid/limited upstream APIs.</summary>
public static class ChatRequestLimits
{
    public const int MaxMessages = 40;
    public const int MaxTextLength = 2000;
    public const int MaxStateLength = 2000;
    public const int MaxBodyBytes = 100_000;

    /// <summary>Returns an error message, or null if the request is acceptable.</summary>
    public static string? Check(ChatRequest? request)
    {
        if (request?.Messages is not { Count: > 0 } messages) return "At least one message is required.";
        if (messages.Count > MaxMessages) return $"A conversation may have at most {MaxMessages} messages. Please start a new chat.";
        if (messages[^1].Role != "user") return "The last message must come from the user.";

        foreach (var m in messages)
        {
            if (m.Role is not ("user" or "agent")) return $"Unknown role '{m.Role}'.";
            if (string.IsNullOrWhiteSpace(m.Text)) return "Messages must not be empty.";
            if (m.Text.Length > MaxTextLength) return $"Messages may have at most {MaxTextLength} characters.";
        }

        if (request.Label is not null && JsonSerializer.Serialize(request.Label).Length > MaxStateLength)
            return "The label specification is too large.";
        return null;
    }
}

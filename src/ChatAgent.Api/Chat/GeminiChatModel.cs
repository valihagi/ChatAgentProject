using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ChatAgent.Api.Chat;

public class GeminiChatModel(HttpClient http, IConfiguration config) : IChatModel
{
    private readonly string _model = config["Gemini:Model"] ?? "gemini-3.8-flash";
    private readonly string _apiKey = config["GEMINI_API_KEY"]
        ?? throw new InvalidOperationException("GEMINI_API_KEY is not set.");

    private readonly string _systemPrompt = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Prompts", "system-prompt.md"));

    private const int MaxRetries = 2;

    /// <summary>Base delay between retries; tests set it to zero.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = _systemPrompt } } },
            generationConfig = new { responseMimeType = "application/json" },
            contents = history.Select(m => new
            {
                role = m.Role == "user" ? "user" : "model",
                parts = new[] { new { text = m.Text } }
            })
        };

        // Free-tier Gemini often answers 503 (overloaded) or 429 (rate limit); retry briefly.
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add("x-goog-api-key", _apiKey);

            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);

            if (response.IsSuccessStatusCode)
                return json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>() ?? "";

            var retryable = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests;
            if (!retryable || attempt >= MaxRetries)
                throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}: {json?["error"]?["message"]}");

            await Task.Delay(RetryDelay * (attempt + 1), ct);
        }
    }
}

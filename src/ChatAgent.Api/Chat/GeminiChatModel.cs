using System.Globalization;
using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChatAgent.Api.Agent;
using ChatAgent.Api.Barcode;

namespace ChatAgent.Api.Chat;

public class GeminiChatModel(HttpClient http, IConfiguration config, TimeProvider time, ILogger<GeminiChatModel> log) : IChatModel
{
    private readonly string _model = config["Gemini:Model"] ?? "gemini-3.5-flash-lite";
    private readonly string _apiKey = config["GEMINI_API_KEY"]
        ?? throw new InvalidOperationException("GEMINI_API_KEY is not set.");

    private readonly string _promptTemplate = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Prompts", "system-prompt.md"));

    private const int MaxRetries = 1; // free tier allows ~20 requests/day/model; every attempt may count

    /// <summary>Base delay between retries; tests set it to zero.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime).ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture);

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = _promptTemplate.Replace("{{today}}", today) } } },
            // Low temperature: this is extraction, not creative writing. The schema guarantees parseable output.
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = ResponseSchema,
                temperature = 0.2,
                maxOutputTokens = 4096,
            },
            contents = history.Select(m => new
            {
                role = m.Role == "user" ? "user" : "model",
                parts = new[] { new { text = m.Text } }
            })
        };

        // Free-tier Gemini is often overloaded (503): retry once. A 429 is a quota limit that a
        // few seconds will not clear, and every retry would count against it, so it fails immediately.
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add("x-goog-api-key", _apiKey);

            var started = Stopwatch.GetTimestamp();
            using var response = await SendAsync(request, ct);
            var json = ParseJson(await response.Content.ReadAsStringAsync(ct));
            log.LogInformation("Gemini {Model} answered {Status} in {Elapsed:F0} ms",
                _model, (int)response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (response.IsSuccessStatusCode)
                return json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>() ?? "";

            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || attempt >= MaxRetries)
                throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}: {json?["error"]?["message"]}");

            log.LogWarning("Gemini overloaded (503), retrying");
            await Task.Delay(RetryDelay * (attempt + 1), ct);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, ct); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) // HttpClient timeout
        {
            throw new HttpRequestException("Gemini did not answer in time.");
        }
    }

    /// <summary>Error bodies from proxies or gateways are not always JSON.</summary>
    private static JsonNode? ParseJson(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    // Mirrors AgentReply / LabelSpec; enums come from the same lists the validator uses.
    private static readonly JsonNode ResponseSchema = JsonNode.Parse($$"""
    {
      "type": "OBJECT",
      "required": ["message", "status", "issues", "label", "cleared"],
      "properties": {
        "message": { "type": "STRING" },
        "status": { "type": "STRING", "enum": ["needs_info", "ready"] },
        "issues": {
          "type": "ARRAY",
          "items": {
            "type": "OBJECT",
            "required": ["field", "kind", "detail"],
            "properties": {
              "field": { "type": "STRING" },
              "kind": { "type": "STRING", "enum": ["missing", "conflict", "invalid"] },
              "detail": { "type": "STRING" }
            }
          }
        },
        "cleared": { "type": "ARRAY", "items": { "type": "STRING", "enum": {{JsonSerializer.Serialize(LabelSpec.FieldNames)}} } },
        "label": {
          "type": "OBJECT",
          "required": {{JsonSerializer.Serialize(LabelSpec.FieldNames)}},
          "properties": {
            "productName": { "type": "STRING", "nullable": true, "description": "Product name as given by the user" },
            "netVolume": { "type": "STRING", "nullable": true, "description": "Net volume with unit ml, cl or l, e.g. 0,75 l. Required on consumer units" },
            "alcoholic": { "type": "BOOLEAN", "nullable": true, "description": "true for beer, wine, spirits, cider, alcoholic mixes; false for juice, water, soft drinks and alcohol-free variants; null if unclear" },
            "alcoholPercent": { "type": "NUMBER", "nullable": true, "description": "Alcohol by volume in % vol as a number, e.g. 12.5" },
            "packagingLevel": { "type": "STRING", "nullable": true, "enum": ["consumer_unit", "case", "pallet"], "description": "bottle/can = consumer_unit, carton/crate/tray = case" },
            "symbology": { "type": "STRING", "nullable": true, "enum": {{JsonSerializer.Serialize(BarcodeTypes.Allowed.Order())}}, "description": "Barcode type the user named, else the default for the packaging level" },
            "gtin": { "type": "STRING", "nullable": true, "description": "Digits of the GTIN/EAN the user gave; fill it whenever a number is present, even if a question is open" },
            "batch": { "type": "STRING", "nullable": true, "description": "Batch or lot (Charge)" },
            "bestBefore": { "type": "STRING", "nullable": true, "description": "Best-before date (MHD) as YYYY-MM-DD; fill it even if it is in the past" },
            "allowPastDate": { "type": "BOOLEAN", "nullable": true, "description": "true only if the user confirmed a past date is intended" },
            "itemCount": { "type": "INTEGER", "nullable": true, "description": "Items per case" },
            "sscc": { "type": "STRING", "nullable": true, "description": "18-digit SSCC for pallets" },
            "url": { "type": "STRING", "nullable": true, "description": "https URL for plain QR/DataMatrix only" },
            "widthMm": { "type": "NUMBER", "nullable": true, "description": "Label width in mm, only if the user stated a size" },
            "heightMm": { "type": "NUMBER", "nullable": true, "description": "Label height in mm, only if the user stated a size" }
          }
        }
      }
    }
    """)!;
}

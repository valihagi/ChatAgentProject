using System.Globalization;
using System.Text.Json;
using ChatAgent.Api.Barcode;
using ChatAgent.Api.Chat;

namespace ChatAgent.Api.Agent;

public class AgentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// One chat turn: LLM extracts/asks -> backend validates -> barcode API renders.
/// If the LLM declares the label ready but validation disagrees, the findings go back to the LLM once
/// so it can phrase the question in the user's language. That second answer is never rendered: any
/// value it "fixed" (e.g. a corrected check digit) was not confirmed by the user.
/// </summary>
public class LabelAgent(IChatModel model, IBarcodeClient barcodes, TimeProvider time, ILogger<LabelAgent> log)
{
    public async Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct)
    {
        var history = WithState(request);
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);

        var raw = await CompleteAsync(history, ct);
        var reply = Parse(raw);
        reply = reply with { Label = LabelSpec.Merge(request.Label, reply.Label, reply.Cleared) };
        log.LogInformation("Turn with {Messages} messages: model says {Status}, {Issues} issues", request.Messages.Count, reply.Status, reply.Issues.Count);

        if (reply.Status != "ready" || reply.Issues.Count > 0)
            return new(reply.Message, "needs_info", reply.Label, null);

        var result = LabelValidator.Validate(reply.Label, today);
        var issues = result.Ok ? await SizeIssuesAsync(result.Request!, ct) : result.Issues;
        if (issues.Count == 0) return await RenderAsync(reply, result, ct);

        // Fields only, never values: the log must not contain user input.
        log.LogWarning("Validation rejected a 'ready' label: {Findings}", string.Join(", ", issues.Select(i => $"{i.Field}:{i.Kind}")));

        // Keep the label exactly as the user gave it, whatever the second answer contains.
        var retry = Parse(await CompleteAsync([.. history, new("agent", raw), new("user", FeedbackFor(issues))], ct));
        log.LogInformation("Feedback round: model answered {Status}", retry.Status);
        if (retry.Status == "needs_info")
            return new(retry.Message, "needs_info", reply.Label, null);

        log.LogWarning("Model still claimed 'ready' after feedback; showing validator findings instead");
        var text = "The label cannot be created yet:\n" + string.Join("\n", issues.Select(i => $"• {i.Detail}"));
        return new(text, "needs_info", reply.Label, null);
    }

    /// <summary>
    /// With an explicit label size the API scales any data into the box, even to an unscannable size.
    /// We ask the API itself how wide the symbol is at the smallest acceptable bar width (an estimate from the
    /// symbol structure was off by up to 40 %) and report a conflict if the requested area is smaller.
    /// </summary>
    private async Task<List<AgentIssue>> SizeIssuesAsync(BarcodeRequest request, CancellationToken ct)
    {
        if (request is not { Unit: "fit", Width: { } width, Height: { } height }) return [];

        var minModule = LabelValidator.MinModuleWidthMm(request.Code);
        var probe = await RenderImageAsync(request with { Unit = "mm", ModuleWidth = minModule, Width = null, Height = null }, ct);
        if (PngInfo.WidthPx(probe.Content) is not { } px) return [];

        var naturalMm = px / (double)LabelValidator.Dpi * 25.4;
        var availableMm = LabelValidator.Is2D(request.Code) ? Math.Min(width, height) : width; // 2D symbols are square
        if (naturalMm <= availableMm * 1.02) return [];

        var detail = string.Create(CultureInfo.InvariantCulture,
            $"{request.Code} with this data needs at least {Math.Ceiling(naturalMm)} mm of width (bars of {minModule} mm, the smallest scannable size), but the requested label area is only {availableMm} mm. Use a larger label or less data.");
        return [new("widthMm", "conflict", detail)];
    }

    private async Task<BarcodeImage> RenderImageAsync(BarcodeRequest barcode, CancellationToken ct)
    {
        try { return await barcodes.GenerateAsync(barcode, ct); }
        catch (BarcodeException ex)
        {
            log.LogWarning("Barcode rendering failed: {Reason}", ex.Message);
            throw new AgentException($"The barcode service could not create the label. {ex.Message}", ex);
        }
    }

    private async Task<ChatResponse> RenderAsync(AgentReply reply, ValidationResult validated, CancellationToken ct)
    {
        var image = await RenderImageAsync(validated.Request!, ct);
        var dataUrl = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Content)}";
        log.LogInformation("Rendered {Symbology} label ({Bytes} bytes)", validated.Request!.Code, image.Content.Length);

        // Be transparent when we derived a value the user did not type.
        var notices = validated.Label.Gtin != reply.Label.Gtin ? new[] { Notice.GtinCompleted } : [];
        return new(reply.Message, "ready", validated.Label, dataUrl, Notices: notices);
    }

    private async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        try { return await model.CompleteAsync(history, ct); }
        catch (HttpRequestException ex)
        {
            log.LogWarning("Language model call failed: {Reason}", ex.Message);
            throw new AgentException($"The language model is unavailable. {ex.Message}", ex);
        }
    }

    private static AgentReply Parse(string raw)
    {
        try
        {
            var reply = JsonSerializer.Deserialize<AgentReply>(raw, JsonSerializerOptions.Web)
                        ?? throw new JsonException("empty");
            return reply with
            {
                Status = (reply.Status ?? "").Trim().ToLowerInvariant(),
                Issues = reply.Issues ?? [],
                Cleared = reply.Cleared ?? [],
                Label = (reply.Label ?? new()).Normalized(),
            };
        }
        catch (JsonException ex)
        {
            throw new AgentException("The language model returned an unexpected answer. Please rephrase and try again.", ex);
        }
    }

    /// <summary>Gives the LLM the spec from the previous turn, since it cannot see its earlier JSON.</summary>
    private static List<ChatMessage> WithState(ChatRequest request)
    {
        var history = request.Messages.ToList();
        if (request.Label is not null && history[^1].Role == "user")
        {
            var state = JsonSerializer.Serialize(request.Label, JsonSerializerOptions.Web);
            history[^1] = history[^1] with { Text = $"{history[^1].Text}\n\n[current label specification]\n{state}" };
        }
        return history;
    }

    private static string FeedbackFor(List<AgentIssue> issues) =>
        "[backend validation] The specification is not valid yet. Set status to needs_info, list these as issues and ask the user in their language:\n"
        + string.Join("\n", issues.Select(i => $"- {i.Field} ({i.Kind}): {i.Detail}"));
}

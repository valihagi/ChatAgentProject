using System.Text.Json;
using ChatAgent.Api.Agent;
using ChatAgent.Api.Barcode;
using ChatAgent.Api.Chat;
using Microsoft.Extensions.Time.Testing;

namespace ChatAgent.Tests;

public class LabelAgentTests
{
    private class ScriptedModel(params AgentReply[] replies) : IChatModel
    {
        private int _next;
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
        {
            Calls.Add(history);
            var reply = replies[Math.Min(_next++, replies.Length - 1)];
            return Task.FromResult(JsonSerializer.Serialize(reply, JsonSerializerOptions.Web));
        }
    }

    private class FakeBarcodes : IBarcodeClient
    {
        public List<BarcodeRequest> Requests { get; } = [];
        public bool Fail { get; init; }

        public Task<BarcodeImage> GenerateAsync(BarcodeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Fail ? throw new BarcodeException("nope") : Task.FromResult(new BarcodeImage([1, 2], "image/png"));
        }
    }

    private static readonly LabelSpec GoodLabel = new()
    {
        ProductName = "Apfelsaft", PackagingLevel = "consumer_unit", Symbology = "EAN13", Gtin = "4006381333931",
    };

    private static LabelAgent Agent(IChatModel model, IBarcodeClient barcodes) =>
        new(model, barcodes, new FakeTimeProvider(new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero)));

    private static ChatRequest Say(string text, LabelSpec? label = null) => new([new("user", text)], label);

    [Fact]
    public async Task Needs_info_reply_is_passed_through_without_calling_the_barcode_api()
    {
        var model = new ScriptedModel(new AgentReply { Message = "GTIN?", Issues = [new("gtin", "missing", "x")] });
        var barcodes = new FakeBarcodes();

        var response = await Agent(model, barcodes).HandleAsync(Say("Apfelsaft"), default);

        Assert.Equal(("needs_info", "GTIN?", null), (response.Status, response.Reply, response.Image));
        Assert.Empty(barcodes.Requests);
    }

    [Fact]
    public async Task Ready_and_valid_renders_the_label_as_data_url()
    {
        var model = new ScriptedModel(new AgentReply { Message = "Done", Status = "ready", Label = GoodLabel });
        var barcodes = new FakeBarcodes();

        var response = await Agent(model, barcodes).HandleAsync(Say("go"), default);

        Assert.Equal("ready", response.Status);
        Assert.Equal("data:image/png;base64,AQI=", response.Image);
        Assert.Equal("4006381333931", barcodes.Requests.Single().Data);
    }

    [Fact]
    public async Task Validation_failure_is_fed_back_once_and_the_second_answer_is_used()
    {
        var wrong = GoodLabel with { Gtin = "4006381333932" };
        var model = new ScriptedModel(
            new AgentReply { Message = "Done", Status = "ready", Label = wrong },
            new AgentReply { Message = "Die Prüfziffer stimmt nicht.", Issues = [new("gtin", "invalid", "x")] });
        var barcodes = new FakeBarcodes();

        var response = await Agent(model, barcodes).HandleAsync(Say("go"), default);

        Assert.Equal(("needs_info", "Die Prüfziffer stimmt nicht."), (response.Status, response.Reply));
        Assert.Equal(2, model.Calls.Count);
        Assert.StartsWith("[backend validation]", model.Calls[1][^1].Text);
        Assert.Empty(barcodes.Requests);
        Assert.Equal("4006381333932", response.Label.Gtin); // state keeps what the user gave
    }

    [Fact]
    public async Task Value_silently_corrected_on_the_feedback_round_is_never_rendered()
    {
        // The model "helpfully" swaps in the expected check digit and calls the label ready again.
        var wrong = GoodLabel with { Gtin = "4006381333932" };
        var model = new ScriptedModel(
            new AgentReply { Message = "Done", Status = "ready", Label = wrong },
            new AgentReply { Message = "Fixed it for you.", Status = "ready", Label = GoodLabel });
        var barcodes = new FakeBarcodes();

        var response = await Agent(model, barcodes).HandleAsync(Say("go"), default);

        Assert.Equal(("needs_info", null), (response.Status, response.Image));
        Assert.Empty(barcodes.Requests);
        Assert.Equal("4006381333932", response.Label.Gtin);
        Assert.Contains("wrong check digit", response.Reply);
    }

    [Fact]
    public async Task Stubborn_model_falls_back_to_validator_findings()
    {
        var wrong = GoodLabel with { Gtin = "4006381333932" };
        var model = new ScriptedModel(new AgentReply { Message = "Done", Status = "ready", Label = wrong });

        var response = await Agent(model, new FakeBarcodes()).HandleAsync(Say("go"), default);

        Assert.Equal("needs_info", response.Status);
        Assert.Contains("wrong check digit", response.Reply);
        Assert.Equal(2, model.Calls.Count);
    }

    [Fact]
    public async Task Ready_reply_reports_the_dpi_and_announces_a_computed_check_digit()
    {
        var twelve = GoodLabel with { Gtin = "400638133393" };
        var model = new ScriptedModel(new AgentReply { Message = "Done", Status = "ready", Label = twelve });

        var response = await Agent(model, new FakeBarcodes()).HandleAsync(Say("go"), default);

        Assert.Equal(300, response.Dpi);
        Assert.Equal("4006381333931", response.Label.Gtin);
        Assert.EndsWith("GTIN completed with check digit: 4006381333931", response.Reply);
    }

    [Fact]
    public async Task Messy_model_output_is_normalized_before_validation()
    {
        var messy = new LabelSpec
        {
            ProductName = " Apfelsaft ", PackagingLevel = "Consumer-Unit", Symbology = "ean13",
            Gtin = "4006 3813 33931", Batch = "", Sscc = "  ",
        };
        var model = new ScriptedModel(new AgentReply { Message = "Done", Status = " Ready ", Label = messy });
        var barcodes = new FakeBarcodes();

        var response = await Agent(model, barcodes).HandleAsync(Say("go"), default);

        Assert.Equal("ready", response.Status);
        Assert.Equal(("EAN13", "consumer_unit", "Apfelsaft", null), (response.Label.Symbology, response.Label.PackagingLevel, response.Label.ProductName, response.Label.Batch));
        Assert.Equal("4006381333931", barcodes.Requests.Single().Data);
    }

    [Fact]
    public async Task Json_with_null_label_and_issues_does_not_crash()
    {
        var response = await Agent(new RawModel("""{"message":"hi","status":"needs_info","issues":null,"label":null}"""),
            new FakeBarcodes()).HandleAsync(Say("go"), default);

        Assert.Equal("hi", response.Reply);
    }

    [Fact]
    public async Task Previous_label_state_is_attached_to_the_last_user_message()
    {
        var model = new ScriptedModel(new AgentReply { Message = "ok" });

        await Agent(model, new FakeBarcodes()).HandleAsync(Say("nimm 0,33 l", GoodLabel), default);

        var sent = model.Calls.Single()[^1].Text;
        Assert.StartsWith("nimm 0,33 l", sent);
        Assert.Contains("\"productName\":\"Apfelsaft\"", sent);
    }

    [Fact]
    public async Task Barcode_service_failure_becomes_an_agent_exception()
    {
        var model = new ScriptedModel(new AgentReply { Message = "Done", Status = "ready", Label = GoodLabel });

        await Assert.ThrowsAsync<AgentException>(() =>
            Agent(model, new FakeBarcodes { Fail = true }).HandleAsync(Say("go"), default));
    }

    [Fact]
    public async Task Invalid_model_json_becomes_an_agent_exception()
    {
        var model = new RawModel("not json");

        await Assert.ThrowsAsync<AgentException>(() =>
            Agent(model, new FakeBarcodes()).HandleAsync(Say("go"), default));
    }

    private class RawModel(string raw) : IChatModel
    {
        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct) => Task.FromResult(raw);
    }
}

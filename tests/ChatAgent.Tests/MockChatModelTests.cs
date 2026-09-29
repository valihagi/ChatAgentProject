using System.Text.Json;
using ChatAgent.Api.Agent;
using ChatAgent.Api.Chat;

namespace ChatAgent.Tests;

public class MockChatModelTests
{
    private static async Task<AgentReply> Ask(params (string Role, string Text)[] history)
    {
        var raw = await new MockChatModel().CompleteAsync(history.Select(h => new ChatMessage(h.Role, h.Text)).ToList(), default);
        return JsonSerializer.Deserialize<AgentReply>(raw, JsonSerializerOptions.Web)!; // must speak the agent's JSON protocol
    }

    [Fact]
    public async Task Asks_for_the_gtin_while_none_was_given()
    {
        var reply = await Ask(("user", "Apfelsaft"));

        Assert.Equal("needs_info", reply.Status);
        Assert.Contains(reply.Issues, i => i.Field == "gtin" && i.Kind == "missing");
    }

    [Fact]
    public async Task A_13_digit_gtin_makes_a_consumer_unit_label_ready()
    {
        var reply = await Ask(("user", "GTIN 4006381333931"));

        Assert.Equal("ready", reply.Status);
        Assert.Equal(("consumer_unit", "EAN13", "4006381333931"), (reply.Label.PackagingLevel, reply.Label.Symbology, reply.Label.Gtin));
        Assert.Equal("0,5 l", reply.Label.NetVolume); // consumer units must state a volume
    }

    [Fact]
    public async Task A_14_digit_gtin_makes_a_case_label()
    {
        var reply = await Ask(("user", "GTIN 14006381333938"));

        Assert.Equal(("case", "EAN14"), (reply.Label.PackagingLevel, reply.Label.Symbology));
    }

    [Fact]
    public async Task The_gtin_is_found_in_earlier_user_messages_and_the_latest_wins()
    {
        var reply = await Ask(("user", "GTIN 4006381333931"), ("agent", "ok"), ("user", "nimm lieber 4012345678901"), ("agent", "ok"), ("user", "danke"));

        Assert.Equal("4012345678901", reply.Label.Gtin);
    }

    [Fact]
    public async Task Backend_validation_feedback_gets_a_needs_info_answer()
    {
        var reply = await Ask(("user", "GTIN 4006381333932"), ("agent", "{}"), ("user", "[backend validation] wrong check digit"));

        Assert.Equal("needs_info", reply.Status);
    }
}

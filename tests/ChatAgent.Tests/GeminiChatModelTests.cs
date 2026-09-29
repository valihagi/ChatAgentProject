using System.Text;
using System.Text.Json.Nodes;
using ChatAgent.Api.Chat;
using Microsoft.Extensions.Configuration;

namespace ChatAgent.Tests;

public class GeminiChatModelTests
{
    [Fact]
    public async Task Sends_system_prompt_history_and_api_key_header_then_parses_reply()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage
        {
            Content = new StringContent(
                """{"candidates":[{"content":{"parts":[{"text":"hi"}]}}]}""", Encoding.UTF8, "application/json"),
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection([new("GEMINI_API_KEY", "k")]).Build();
        var model = new GeminiChatModel(new HttpClient(handler), config);

        var reply = await model.CompleteAsync(
            [new("user", "Hallo"), new("agent", "Grüß dich"), new("user", "Apfelsaft")], default);

        Assert.Equal("hi", reply);
        Assert.Equal("k", handler.Request!.Headers.GetValues("x-goog-api-key").Single());
        var body = JsonNode.Parse(handler.RequestBody!)!;
        Assert.Contains("label assistant", body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("application/json", body["generationConfig"]!["responseMimeType"]!.GetValue<string>());
        Assert.Equal(["user", "model", "user"], body["contents"]!.AsArray().Select(c => c!["role"]!.GetValue<string>()));
    }
}

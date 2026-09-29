using System.Net;
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

    [Fact]
    public async Task Retries_on_503_and_gives_up_with_the_api_message()
    {
        var calls = 0;
        var handler = new FakeHandler(_ =>
        {
            calls++;
            return calls < 3
                ? Json(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"busy"}}""")
                : Json(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""");
        });
        var model = Model(handler);

        Assert.Equal("ok", await model.CompleteAsync([new("user", "x")], default));
        Assert.Equal(3, calls);

        var always503 = Model(new FakeHandler(_ => Json(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"busy"}}""")));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => always503.CompleteAsync([new("user", "x")], default));
        Assert.Contains("busy", ex.Message);
    }

    [Fact]
    public async Task Does_not_retry_client_errors()
    {
        var calls = 0;
        var model = Model(new FakeHandler(_ => { calls++; return Json(HttpStatusCode.BadRequest, """{"error":{"message":"bad"}}"""); }));

        await Assert.ThrowsAsync<HttpRequestException>(() => model.CompleteAsync([new("user", "x")], default));
        Assert.Equal(1, calls);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static GeminiChatModel Model(FakeHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection([new("GEMINI_API_KEY", "k")]).Build())
    { RetryDelay = TimeSpan.Zero };
}

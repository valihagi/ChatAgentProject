using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ChatAgent.Api.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

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
        var model = new GeminiChatModel(new HttpClient(handler), config, Clock);

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
    public async Task Retries_once_on_503_and_then_gives_up_with_the_api_message()
    {
        var calls = 0;
        var handler = new FakeHandler(_ =>
        {
            calls++;
            return calls < 2
                ? Json(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"busy"}}""")
                : Json(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""");
        });
        var model = Model(handler);

        Assert.Equal("ok", await model.CompleteAsync([new("user", "x")], default));
        Assert.Equal(2, calls);

        var always503 = Model(new FakeHandler(_ => Json(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"busy"}}""")));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => always503.CompleteAsync([new("user", "x")], default));
        Assert.Contains("busy", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)] // quota: retrying within seconds only burns more of it
    public async Task Does_not_retry_client_errors_or_quota_limits(HttpStatusCode status)
    {
        var calls = 0;
        var model = Model(new FakeHandler(_ => { calls++; return Json(status, """{"error":{"message":"no"}}"""); }));

        await Assert.ThrowsAsync<HttpRequestException>(() => model.CompleteAsync([new("user", "x")], default));
        Assert.Equal(1, calls);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static GeminiChatModel Model(FakeHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection([new("GEMINI_API_KEY", "k")]).Build(),
        Clock)
    { RetryDelay = TimeSpan.Zero };

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Sends_todays_date_low_temperature_and_a_response_schema()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"{}"}]}}]}"""));

        await Model(handler).CompleteAsync([new("user", "x")], default);

        var body = JsonNode.Parse(handler.RequestBody!)!;
        var system = body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Today is 2026-09-29 (Tuesday)", system);
        Assert.DoesNotContain("{{today}}", system);

        var config = body["generationConfig"]!;
        Assert.Equal(0.2, config["temperature"]!.GetValue<double>());
        Assert.Equal("OBJECT", config["responseSchema"]!["type"]!.GetValue<string>());
        var symbologies = config["responseSchema"]!["properties"]!["label"]!["properties"]!["symbology"]!["enum"]!.AsArray();
        Assert.Contains("GS1-128", symbologies.Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task Non_json_error_body_becomes_http_request_exception()
    {
        var model = Model(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Bad gateway</html>", Encoding.UTF8, "text/html"),
        }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => model.CompleteAsync([new("user", "x")], default));
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task Http_client_timeout_becomes_http_request_exception()
    {
        var model = Model(new FakeHandler(_ => throw new TaskCanceledException("timeout")));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => model.CompleteAsync([new("user", "x")], default));
        Assert.Contains("in time", ex.Message);
    }
}

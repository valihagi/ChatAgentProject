using System.Net;

namespace ChatAgent.Tests;

/// <summary>Records the request and returns a canned response, so tests never touch the network.</summary>
public class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Request = request;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return respond(request);
    }

    public static HttpResponseMessage Bytes(string contentType, byte[]? body = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new ByteArrayContent(body ?? [1, 2, 3]);
        content.Headers.ContentType = new(contentType);
        return new HttpResponseMessage(status) { Content = content };
    }
}

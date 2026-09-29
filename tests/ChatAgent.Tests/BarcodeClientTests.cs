using System.Net;
using ChatAgent.Api.Barcode;
using Microsoft.Extensions.Configuration;

namespace ChatAgent.Tests;

public class BarcodeClientTests
{
    private const string AccessId = "secret-id";

    private static BarcodeClient Client(FakeHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection([new("TECIT_ACCESS_ID", AccessId)]).Build());

    private static readonly BarcodeRequest Ean = new("EAN13", "4006381333931");

    [Fact]
    public async Task Returns_image_and_sends_access_id_in_body_not_url()
    {
        var handler = new FakeHandler(_ => FakeHandler.Bytes("image/png", [9, 9]));

        var image = await Client(handler).GenerateAsync(Ean, default);

        Assert.Equal("image/png", image.ContentType);
        Assert.Equal([9, 9], image.Content);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.DoesNotContain(AccessId, handler.Request.RequestUri!.ToString());
        Assert.Contains($"accessid={AccessId}", handler.RequestBody);
        Assert.Contains("code=EAN13", handler.RequestBody);
    }

    [Fact]
    public async Task Error_bitmap_with_status_200_is_reported_as_failure()
    {
        // The API answers 200 + image/gif when the request is invalid or rate limited.
        var handler = new FakeHandler(_ => FakeHandler.Bytes("image/gif"));

        await Assert.ThrowsAsync<BarcodeException>(() => Client(handler).GenerateAsync(Ean, default));
    }

    [Fact]
    public async Task Http_error_status_is_reported_as_failure()
    {
        var handler = new FakeHandler(_ => FakeHandler.Bytes("text/plain", status: HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<BarcodeException>(() => Client(handler).GenerateAsync(Ean, default));
        Assert.DoesNotContain(AccessId, ex.Message);
    }

    [Fact]
    public async Task Network_failure_and_timeout_become_barcode_exceptions()
    {
        await Assert.ThrowsAsync<BarcodeException>(() =>
            Client(new FakeHandler(_ => throw new HttpRequestException("down"))).GenerateAsync(Ean, default));
        await Assert.ThrowsAsync<BarcodeException>(() =>
            Client(new FakeHandler(_ => throw new TaskCanceledException("timeout"))).GenerateAsync(Ean, default));
    }
}

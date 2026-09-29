namespace ChatAgent.Api.Barcode;

public record BarcodeImage(byte[] Content, string ContentType);

public class BarcodeException(string message) : Exception(message);

public interface IBarcodeClient
{
    Task<BarcodeImage> GenerateAsync(BarcodeRequest request, CancellationToken ct);
}

public class BarcodeClient(HttpClient http, IConfiguration config, ILogger<BarcodeClient> log) : IBarcodeClient
{
    private const string Endpoint = "https://barcode.tec-it.com/barcode.ashx";

    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png", ["jpg"] = "image/jpeg", ["gif"] = "image/gif", ["svg"] = "image/svg+xml",
    };

    private readonly string _accessId = config["TECIT_ACCESS_ID"]
        ?? throw new InvalidOperationException("TECIT_ACCESS_ID is not set.");

    public async Task<BarcodeImage> GenerateAsync(BarcodeRequest request, CancellationToken ct)
    {
        // POST keeps the access id out of URLs and logs.
        var form = request.ToForm();
        form["accessid"] = _accessId;
        form["onerror"] = "500";

        byte[] bytes;
        string contentType;
        try
        {
            using var response = await http.PostAsync(Endpoint, new FormUrlEncodedContent(form), ct);
            if (!response.IsSuccessStatusCode)
                throw new BarcodeException($"Barcode API returned {(int)response.StatusCode}.");
            bytes = await response.Content.ReadAsByteArrayAsync(ct);
            contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning("Barcode API not reachable: {Reason}", ex.Message);
            throw new BarcodeException("Barcode API is not reachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) // HttpClient timeout
        {
            log.LogWarning("Barcode API timed out");
            throw new BarcodeException("Barcode API did not answer in time.");
        }

        // Observed: despite onerror=500 the API answers 200 with an error *bitmap* (image/gif, text
        // rendered into the image, e.g. "Wrong check digit" or the rate-limit notice). A different
        // media type than requested therefore means the request failed.
        if (!MediaTypes.TryGetValue(request.Format, out var expected) || contentType != expected)
        {
            log.LogWarning("Barcode API returned {ContentType} instead of an image for {Code}: invalid data or rate limit", contentType, request.Code);
            throw new BarcodeException(
                "Barcode API rejected the request (invalid data for this barcode type, unsupported option, or rate limit).");
        }

        return new BarcodeImage(bytes, contentType);
    }
}

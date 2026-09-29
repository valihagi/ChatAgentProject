using System.Threading.RateLimiting;
using ChatAgent.Api.Agent;
using ChatAgent.Api.Barcode;
using ChatAgent.Api.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = ChatRequestLimits.MaxBodyBytes);

builder.Services.AddChatModel(builder.Configuration); // Mock unless Chat:Provider=Gemini
builder.Services.AddHttpClient<IBarcodeClient, BarcodeClient>(c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<LabelAgent>();

// Every chat turn costs upstream quota (Gemini + Barcode API), so limit turns per client IP.
var chatPerMinute = builder.Configuration.GetValue("RateLimit:ChatPerMinute", 12);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, ct) => new(context.HttpContext.Response.WriteAsJsonAsync(
        new { error = "Too many requests. Please wait a moment and try again." }, ct));
    o.AddPolicy("chat", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = chatPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Unexpected failures: log them, but answer with JSON the frontend can show (no stack traces).
app.UseExceptionHandler(errors => errors.Run(context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    return context.Response.WriteAsJsonAsync(new { error = "Unexpected server error. Please try again." });
}));

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapPost("/api/chat", async (ChatRequest request, LabelAgent agent, CancellationToken ct) =>
{
    if (ChatRequestLimits.Check(request) is { } problem)
        return Results.BadRequest(new { error = problem });

    try
    {
        return Results.Ok(await agent.HandleAsync(request, ct));
    }
    catch (AgentException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireRateLimiting("chat");

app.Run();

public partial class Program;

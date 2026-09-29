using ChatAgent.Api.Barcode;
using ChatAgent.Api.Chat;

var builder = WebApplication.CreateBuilder(args);

// "Mock" (default) keeps development offline; set Chat:Provider=Gemini to use the real LLM.
if (builder.Configuration["Chat:Provider"] == "Gemini")
    builder.Services.AddHttpClient<IChatModel, GeminiChatModel>();
else
    builder.Services.AddSingleton<IChatModel, MockChatModel>();

builder.Services.AddHttpClient<IBarcodeClient, BarcodeClient>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/chat", async (ChatRequest request, IChatModel model, CancellationToken ct) =>
{
    if (request.Messages is not { Count: > 0 })
        return Results.BadRequest(new { error = "messages must not be empty." });

    var reply = await model.CompleteAsync(request.Messages, ct);
    return Results.Ok(new ChatResponse(reply));
});

app.Run();

public partial class Program;

using ChatAgent.Api.Agent;
using ChatAgent.Api.Barcode;
using ChatAgent.Api.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChatModel(builder.Configuration); // Mock unless Chat:Provider=Gemini
builder.Services.AddHttpClient<IBarcodeClient, BarcodeClient>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<LabelAgent>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/chat", async (ChatRequest request, LabelAgent agent, CancellationToken ct) =>
{
    if (request.Messages is not { Count: > 0 } || request.Messages[^1].Role != "user")
        return Results.BadRequest(new { error = "The last message must come from the user." });

    try
    {
        return Results.Ok(await agent.HandleAsync(request, ct));
    }
    catch (AgentException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

public partial class Program;

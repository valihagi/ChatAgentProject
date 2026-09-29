namespace ChatAgent.Api.Chat;

public static class ChatModelRegistration
{
    /// <summary>Registers the LLM selected by <c>Chat:Provider</c> (Mock, default, or Gemini; case-insensitive).</summary>
    public static IServiceCollection AddChatModel(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Chat:Provider"] ?? "Mock";

        if (provider.Equals("Mock", StringComparison.OrdinalIgnoreCase))
            return services.AddSingleton<IChatModel, MockChatModel>();

        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(config["GEMINI_API_KEY"]))
                throw new InvalidOperationException("Chat:Provider is Gemini but GEMINI_API_KEY is not set.");
            services.AddHttpClient<IChatModel, GeminiChatModel>(c => c.Timeout = TimeSpan.FromSeconds(45));
            return services;
        }

        throw new InvalidOperationException($"Unknown Chat:Provider '{provider}'. Use 'Mock' or 'Gemini'.");
    }
}

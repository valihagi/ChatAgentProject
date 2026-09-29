using ChatAgent.Api.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChatAgent.Tests;

public class ChatModelRegistrationTests
{
    private static IChatModel Resolve(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(config).AddSingleton(TimeProvider.System).AddChatModel(config);
        return services.BuildServiceProvider().GetRequiredService<IChatModel>();
    }

    [Fact]
    public void Defaults_to_mock() => Assert.IsType<MockChatModel>(Resolve());

    [Theory]
    [InlineData("Gemini")]
    [InlineData("gemini")]
    [InlineData("GEMINI")]
    public void Gemini_is_selected_regardless_of_casing(string provider) =>
        Assert.IsType<GeminiChatModel>(Resolve(("Chat:Provider", provider), ("GEMINI_API_KEY", "k")));

    [Fact]
    public void Unknown_provider_fails_at_startup_instead_of_silently_using_the_mock()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(("Chat:Provider", "Gemin")));
        Assert.Contains("Gemin", ex.Message);
    }

    [Fact]
    public void Gemini_without_key_fails_at_startup() =>
        Assert.Throws<InvalidOperationException>(() => Resolve(("Chat:Provider", "Gemini")));
}

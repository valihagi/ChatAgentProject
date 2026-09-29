using ChatAgent.Api.Barcode;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChatAgent.Tests;

public class BarcodeRegistrationTests
{
    [Fact]
    public void Missing_access_id_fails_at_startup_with_setup_hint()
    {
        var config = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddBarcodeClient(config));

        Assert.Contains("user-secrets", ex.Message);
    }

    [Fact]
    public void Present_access_id_registers_the_client()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([new("TECIT_ACCESS_ID", "id")]).Build();
        var provider = new ServiceCollection().AddSingleton<IConfiguration>(config).AddBarcodeClient(config).BuildServiceProvider();

        Assert.IsType<BarcodeClient>(provider.GetRequiredService<IBarcodeClient>());
    }
}

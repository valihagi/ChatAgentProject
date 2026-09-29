namespace ChatAgent.Api.Barcode;

public static class BarcodeRegistration
{
    /// <summary>Registers the TEC-IT client; fails at startup, not on the first chat turn, if the access id is missing.</summary>
    public static IServiceCollection AddBarcodeClient(this IServiceCollection services, IConfiguration config)
    {
        if (string.IsNullOrEmpty(config["TECIT_ACCESS_ID"]))
            throw new InvalidOperationException(
                "TECIT_ACCESS_ID is not set. Provide it as environment variable or with 'dotnet user-secrets set TECIT_ACCESS_ID <id> --project src/ChatAgent.Api' (see README).");

        services.AddHttpClient<IBarcodeClient, BarcodeClient>(c => c.Timeout = TimeSpan.FromSeconds(20));
        return services;
    }
}

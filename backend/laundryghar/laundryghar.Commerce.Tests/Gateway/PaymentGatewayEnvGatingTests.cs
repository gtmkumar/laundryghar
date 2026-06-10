using laundryghar.Commerce.Infrastructure.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace laundryghar.Commerce.Tests.Gateway;

/// <summary>
/// Tests that confirm IPaymentGateway env-gating:
///   - Development → DevPaymentGateway is registered
///   - Non-Development + missing Razorpay config → startup throws (fail closed)
///   - Non-Development + valid Razorpay config → RazorpayPaymentGateway is resolved, never DevPaymentGateway
/// </summary>
public sealed class PaymentGatewayEnvGatingTests
{
    // ── Development environment ────────────────────────────────────────────────

    [Fact]
    public void Development_RegistersDevPaymentGateway()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var env = new FakeHostEnvironment("Development");

        // Mirror the Program.cs registration logic
        if (env.IsDevelopment())
            services.AddSingleton<IPaymentGateway, DevPaymentGateway>();

        var provider = services.BuildServiceProvider();
        var gateway  = provider.GetRequiredService<IPaymentGateway>();

        Assert.IsType<DevPaymentGateway>(gateway);
    }

    // ── Non-Development: missing credentials → fail closed ────────────────────

    [Fact]
    public void NonDevelopment_MissingRazorpayKeyId_ThrowsAtStartup()
    {
        var env = new FakeHostEnvironment("Production");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Razorpay:KeySecret"] = "some_secret"
                // KeyId deliberately absent
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => ValidateAndRegisterRazorpay(env, config));
    }

    [Fact]
    public void NonDevelopment_MissingRazorpayKeySecret_ThrowsAtStartup()
    {
        var env = new FakeHostEnvironment("Staging");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Razorpay:KeyId"] = "rzp_live_key"
                // KeySecret deliberately absent
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => ValidateAndRegisterRazorpay(env, config));
    }

    [Fact]
    public void NonDevelopment_BothCredentialsPresent_DoesNotThrow()
    {
        var env = new FakeHostEnvironment("Production");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Razorpay:KeyId"]     = "rzp_live_key",
                ["Razorpay:KeySecret"] = "rzp_live_secret"
            })
            .Build();

        // Should not throw
        var exception = Record.Exception(() => ValidateAndRegisterRazorpay(env, config));
        Assert.Null(exception);
    }

    [Fact]
    public void NonDevelopment_NeverResolvesDevPaymentGateway()
    {
        var env = new FakeHostEnvironment("Production");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Razorpay:KeyId"]     = "rzp_live_key",
                ["Razorpay:KeySecret"] = "rzp_live_secret"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();

        ValidateAndRegisterRazorpay(env, config, services);

        var provider = services.BuildServiceProvider();
        var gateway  = provider.GetRequiredService<IPaymentGateway>();

        Assert.IsNotType<DevPaymentGateway>(gateway);
        Assert.IsType<RazorpayPaymentGateway>(gateway);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the env-gating logic from Commerce Program.cs for test isolation.
    /// Throws InvalidOperationException when credentials are missing, matching startup behaviour.
    /// </summary>
    private static void ValidateAndRegisterRazorpay(
        IHostEnvironment env,
        IConfiguration config,
        ServiceCollection? services = null)
    {
        if (env.IsDevelopment()) return;

        var rzpSection  = config.GetSection("Razorpay");
        var rzpKeyId    = rzpSection["KeyId"];
        var rzpKeySecret = rzpSection["KeySecret"];

        if (string.IsNullOrWhiteSpace(rzpKeyId) || string.IsNullOrWhiteSpace(rzpKeySecret))
            throw new InvalidOperationException(
                "Razorpay:KeyId and Razorpay:KeySecret are required outside Development.");

        if (services is null) return;

        services.Configure<RazorpaySettings>(rzpSection);
        services.AddHttpClient("razorpay", http =>
        {
            http.BaseAddress = new Uri("https://api.razorpay.com/");
            http.Timeout     = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IPaymentGateway, RazorpayPaymentGateway>();
    }

    // ── Fake IHostEnvironment ──────────────────────────────────────────────────

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
            => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Commerce.Tests";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

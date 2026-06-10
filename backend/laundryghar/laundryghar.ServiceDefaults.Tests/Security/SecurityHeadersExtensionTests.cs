using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace laundryghar.ServiceDefaults.Tests.Security;

/// <summary>
/// Integration-style unit tests for <see cref="Extensions.UseSecurityHeaders"/> using
/// an in-process <see cref="TestServer"/> — no TCP listener, no database.
///
/// Each test builds a minimal <see cref="WebApplication"/> with a single-route terminal
/// handler, drives a GET through the pipeline, and asserts on the response headers.
/// </summary>
public sealed class SecurityHeadersExtensionTests : IAsyncDisposable
{
    private WebApplication? _app;

    // ── Helper ───────────────────────────────────────────────────────────────

    private async Task<HttpClient> BuildClientAsync(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environmentName });

        // Swap Kestrel for an in-process TestServer — no port binding required.
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseSecurityHeaders();
        _app.MapGet("/ping", () => "pong");

        await _app.StartAsync();
        return _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    // ── Non-Development: all four headers must be present ────────────────────

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopment_SetsHsts(string env)
    {
        var client = await BuildClientAsync(env);
        var response = await client.GetAsync("/ping");
        Assert.Equal("max-age=31536000; includeSubDomains",
            response.Headers.GetValues("Strict-Transport-Security").Single());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopment_SetsXContentTypeOptionsNosniff(string env)
    {
        var client = await BuildClientAsync(env);
        var response = await client.GetAsync("/ping");
        Assert.Equal("nosniff",
            response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopment_SetsXFrameOptionsDeny(string env)
    {
        var client = await BuildClientAsync(env);
        var response = await client.GetAsync("/ping");
        Assert.Equal("DENY",
            response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopment_SetsReferrerPolicy(string env)
    {
        var client = await BuildClientAsync(env);
        var response = await client.GetAsync("/ping");
        Assert.Equal("strict-origin-when-cross-origin",
            response.Headers.GetValues("Referrer-Policy").Single());
    }

    // ── Development: no security headers must be added ───────────────────────

    [Fact]
    public async Task Development_DoesNotAddAnySecurityHeader()
    {
        var client = await BuildClientAsync("Development");
        var response = await client.GetAsync("/ping");

        Assert.False(response.Headers.Contains("Strict-Transport-Security"),
            "HSTS header must not be set in Development.");
        Assert.False(response.Headers.Contains("X-Content-Type-Options"),
            "X-Content-Type-Options must not be set in Development.");
        Assert.False(response.Headers.Contains("X-Frame-Options"),
            "X-Frame-Options must not be set in Development.");
        Assert.False(response.Headers.Contains("Referrer-Policy"),
            "Referrer-Policy must not be set in Development.");
    }
}

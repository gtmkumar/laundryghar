using laundryghar.Mcp.Infrastructure.Auth;
using laundryghar.Mcp.Infrastructure.Http;
using laundryghar.Mcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ServiceDefaults (OTel, service discovery, resilience, /health + /alive) ──
builder.AddServiceDefaults();

// ─── Configuration ────────────────────────────────────────────────────────────

var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is required.");

if (string.IsNullOrWhiteSpace(jwtSettings.Authority))
    throw new InvalidOperationException(
        "Jwt:Authority (the Identity issuer base URL whose JWKS publishes the RS256 public key) is required.");

var downstreamSection = builder.Configuration.GetSection(DownstreamServicesConfig.SectionName);
var downstream = downstreamSection.Get<DownstreamServicesConfig>() ?? new DownstreamServicesConfig();

// OAuth 2.1 protected-resource configuration (RFC 9728)
var oauthResourceSection = builder.Configuration.GetSection(OAuthResourceSettings.SectionName);
var oauthResource = oauthResourceSection.Get<OAuthResourceSettings>() ?? new OAuthResourceSettings();
builder.Services.Configure<OAuthResourceSettings>(oauthResourceSection);

// ── Fail-closed: cleartext URLs not permitted outside Development ─────────
// Mirrors the RsaJwtKeyProvider philosophy: the service throws at startup
// rather than silently operating with insecure configuration in staging/prod.
if (!builder.Environment.IsDevelopment())
{
    static void AssertHttps(string url, string configKey)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Configuration error: {configKey} is set to a cleartext http:// URL ('{url}'). " +
                "All base URLs must use https:// outside Development.");
    }

    AssertHttps(downstream.CatalogBaseUrl,   "DownstreamServices:CatalogBaseUrl");
    AssertHttps(downstream.OrdersBaseUrl,    "DownstreamServices:OrdersBaseUrl");
    AssertHttps(oauthResource.McpBaseUrl,    "OAuthResource:McpBaseUrl");
    AssertHttps(oauthResource.IdentityBaseUrl, "OAuthResource:IdentityBaseUrl");
}

// ─── HTTP context ─────────────────────────────────────────────────────────────

builder.Services.AddHttpContextAccessor();

// ─── JWT config ───────────────────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(jwtSection);
builder.Services.Configure<DownstreamServicesConfig>(downstreamSection);

// ─── JWT Authentication (validate-only; RS256 from Identity JWKS) ─────────────
// Identical setup to all other LaundryGhar services.

// Capture oauthResource for use in the OnChallenge closure below.
var mcpBaseUrl = oauthResource.McpBaseUrl.TrimEnd('/');
var resourceMetadataUrl = $"{mcpBaseUrl}/.well-known/oauth-protected-resource";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = jwtSettings.Authority;
        opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Pin to RS256 — reject "none" and HMAC algorithm-confusion attacks.
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
        };

        // RFC 9728 §5 / MCP spec discovery handshake:
        // 401 responses MUST include WWW-Authenticate: Bearer resource_metadata="<url>"
        // so MCP clients (Claude.ai, Claude Code, Gemini CLI) can discover the
        // authorization server without prior configuration.
        opts.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                // Suppress the default WWW-Authenticate header so we can set our own.
                context.HandleResponse();

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                // RFC 9728 §5: Bearer challenge with resource_metadata URL
                context.Response.Headers["WWW-Authenticate"] =
                    $"Bearer resource_metadata=\"{resourceMetadataUrl}\"";

                var body = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    error = "unauthorized",
                    error_description = "Bearer token is required. Discover the authorization server via the WWW-Authenticate header."
                });

                return context.Response.Body.WriteAsync(body).AsTask();
            }
        };
    });

// ─── Authorization — CustomerOnly policy only (no admin endpoints here) ────────

builder.Services.AddSingleton<IAuthorizationHandler, CustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, CustomerOnlyPolicyProvider>();
builder.Services.AddAuthorization();

// ─── Downstream HttpClients (token-forwarding) ────────────────────────────────
// TokenForwardingHandler reads the inbound Authorization header from IHttpContextAccessor
// and attaches it to every outbound request — downstream services validate it themselves.

builder.Services.AddTransient<TokenForwardingHandler>();

builder.Services.AddKeyedSingleton<HttpClient>(
    DownstreamClientNames.Catalog,
    (sp, _) =>
    {
        var opts = sp.GetRequiredService<IOptions<DownstreamServicesConfig>>().Value;
        var handler = new TokenForwardingHandler(sp.GetRequiredService<IHttpContextAccessor>())
        {
            InnerHandler = new HttpClientHandler()
        };
        return new HttpClient(handler) { BaseAddress = new Uri(opts.CatalogBaseUrl) };
    });

builder.Services.AddKeyedSingleton<HttpClient>(
    DownstreamClientNames.Orders,
    (sp, _) =>
    {
        var opts = sp.GetRequiredService<IOptions<DownstreamServicesConfig>>().Value;
        var handler = new TokenForwardingHandler(sp.GetRequiredService<IHttpContextAccessor>())
        {
            InnerHandler = new HttpClientHandler()
        };
        return new HttpClient(handler) { BaseAddress = new Uri(opts.OrdersBaseUrl) };
    });

// ─── MCP Server — Streamable HTTP transport ────────────────────────────────────
// Tools registered from LaundryTools class (annotated with [McpServerToolType]).
// Auth is enforced at the ASP.NET Core middleware layer before MapMcp handles any request.

builder.Services
    .AddMcpServer(opts =>
    {
        opts.ServerInfo = new ModelContextProtocol.Protocol.Implementation
        {
            Name = "laundryghar-mcp",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport()
    .WithTools<LaundryTools>();

// ─── OpenAPI ──────────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// ─── CORS ─────────────────────────────────────────────────────────────────────

builder.Services.AddCors(opts =>
{
    if (builder.Environment.IsDevelopment())
        opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
    else
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        opts.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
    }
});

// ─── Health Checks ────────────────────────────────────────────────────────────
// No DB in this service — only liveness + readiness (no npgsql check needed).

builder.Services.AddHealthChecks();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─── Middleware pipeline ───────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ─── Health endpoints ──────────────────────────────────────────────────────────

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready"),
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var status = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
            ? "Healthy" : "Unhealthy";
        var payload = JsonSerializer.Serialize(new
        {
            status,
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        });
        await ctx.Response.WriteAsync(payload);
    }
});

// ─── OAuth 2.1 Protected Resource Metadata (RFC 9728) ─────────────────────────
// Anonymous GET — MCP clients fetch this to discover the authorization server.
// The resource identifier is the /mcp endpoint; bearer_methods_supported = header.
app.MapGet("/.well-known/oauth-protected-resource",
    (IOptions<OAuthResourceSettings> settings) =>
    {
        var s = settings.Value;
        var mcpBase = s.McpBaseUrl.TrimEnd('/');
        var identityBase = s.IdentityBaseUrl.TrimEnd('/');
        return Results.Json(new
        {
            resource = $"{mcpBase}/mcp",
            authorization_servers = new[] { identityBase },
            bearer_methods_supported = new[] { "header" }
        });
    })
.AllowAnonymous()
.WithTags("Well-Known");

// ─── MCP endpoint — requires CustomerOnly authentication ───────────────────────
// All requests to /mcp must carry a valid customer JWT (token_use=customer).
// The .RequireAuthorization("CustomerOnly") enforces this before the MCP protocol
// layer sees any message.

app.MapMcp("/mcp")
   .RequireAuthorization("CustomerOnly");

// ─── Aspire default health endpoints (/health + /alive) ───────────────────────
app.MapDefaultEndpoints();

app.Run();

public partial class Program { }

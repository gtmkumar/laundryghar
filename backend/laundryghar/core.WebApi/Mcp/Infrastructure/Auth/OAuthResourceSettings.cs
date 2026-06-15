namespace core.WebApi.Mcp.Infrastructure.Auth;

/// <summary>
/// Configuration for the OAuth 2.1 protected-resource metadata (RFC 9728).
/// Injected via appsettings — base URLs default to the dev ports when not provided.
/// </summary>
public sealed class OAuthResourceSettings
{
    public const string SectionName = "OAuthResource";

    /// <summary>
    /// Base URL of this MCP service. Used to build the resource identifier and the
    /// resource_metadata URL placed in the WWW-Authenticate challenge header.
    /// Defaults to http://localhost:5056 — post-consolidation the MCP resource server
    /// runs in-process inside the core host (Identity + Engagement + Mcp).
    /// </summary>
    public string McpBaseUrl { get; set; } = "http://localhost:5056";

    /// <summary>
    /// Base URL of the LaundryGhar Identity authorization server.
    /// Listed in authorization_servers[] of the protected-resource metadata.
    /// Defaults to http://localhost:5056 (dev port).
    /// </summary>
    public string IdentityBaseUrl { get; set; } = "http://localhost:5056";
}

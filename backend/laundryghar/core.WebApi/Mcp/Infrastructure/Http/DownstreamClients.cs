namespace core.WebApi.Mcp.Infrastructure.Http;

/// <summary>
/// Named HttpClient registrations for downstream service calls.
/// The MCP service forwards the inbound customer bearer token on all outbound requests —
/// downstream services enforce their own authorization (CustomerOnly policy).
/// Base URLs are read from configuration to support different environments.
/// In dev, fixed localhost ports match the AppHost-pinned addresses.
/// </summary>
public static class DownstreamClientNames
{
    public const string Catalog = "catalog";
    public const string Orders = "orders";
}

/// <summary>
/// Configuration section for downstream service base URLs.
/// </summary>
public sealed class DownstreamServicesConfig
{
    public const string SectionName = "DownstreamServices";

    // Post-consolidation Catalog + Orders both live in laundryghar.Operations on port 5002.
    public string CatalogBaseUrl { get; set; } = "http://localhost:5002";
    public string OrdersBaseUrl { get; set; } = "http://localhost:5002";
}

using Microsoft.AspNetCore.Authorization;

namespace core.WebApi.Mcp.Infrastructure.Auth;

/// <summary>
/// Authorization requirement for the MCP resource server.
/// Accepted token shapes:
///   - token_use=customer_mcp AND scope contains "mcp:booking"  (OAuth 2.1 path — Claude.ai, Claude Code)
///   - token_use=customer  (direct customer app tokens — backwards-compatible; no scope required)
///
/// System-user, rider, and bare customer_mcp tokens without the mcp:booking scope are rejected.
///
/// NOTE: renamed from the legacy Mcp <c>CustomerOnlyRequirement</c> to avoid colliding with the
/// Identity <c>CustomerOnlyRequirement</c> already registered in laundryghar.Utilities.Auth.
/// </summary>
public sealed class McpCustomerOnlyRequirement : IAuthorizationRequirement { }

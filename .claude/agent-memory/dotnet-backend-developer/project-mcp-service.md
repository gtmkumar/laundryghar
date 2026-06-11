---
name: project-mcp-service
description: MCP service Phase-1 spike — port, NuGet version, auth wiring, DI pattern for keyed HttpClients
metadata:
  type: project
---

laundryghar.Mcp is at backend/laundryghar/laundryghar.Mcp/, port 5009, registered in AppHost as "mcp".

**Why:** Phase-1 spike to expose customer-facing LaundryGhar tools via the Model Context Protocol (Streamable HTTP transport) so an AI assistant can call them on behalf of an authenticated customer.

**How to apply:** When extending or reviewing this service, note:

- NuGet: `ModelContextProtocol.AspNetCore 1.4.0` (pulls in `ModelContextProtocol.Core 1.4.0` and `ModelContextProtocol 1.4.0` transitively). No `ModelContextProtocol.Extensions.DependencyInjection` needed — it's bundled.
- Tool attributes: `[McpServerToolType]` on class, `[McpServerTool(Name = "…", ReadOnly = true)]` + `[Description("…")]` (System.ComponentModel) on methods. The `[McpServerTool]` attribute does NOT have a Description property; use `[Description]` separately.
- `WithTools<LaundryTools>()` generic form (not `WithToolsFromAssembly`) — avoids reflection trimming warning.
- Downstream HttpClients are registered as `AddKeyedSingleton<HttpClient>` (not named via `IHttpClientFactory`) because `LaundryTools` needs them injected via `[FromKeyedServices]`. `TokenForwardingHandler` reads `IHttpContextAccessor` at request time and copies the `Authorization` header to outbound calls.
- Auth: same RS256 JWKS pattern as all other services; `CustomerOnlyPolicyProvider` + `CustomerOnlyHandler` enforce `token_use=customer`. `app.MapMcp("/mcp").RequireAuthorization("CustomerOnly")` enforces this at the ASP.NET Core layer before the MCP protocol processes any message.
- No DB reference — this service has no `SharedDataModel` dependency. HealthChecks has no npgsql check.
- MCP endpoint dev URL: `http://localhost:5009/mcp` (Streamable HTTP, POST for tool calls, GET for SSE stream).
- The AppHost does NOT inject `ConnectionStrings__Default` for this service (no DB needed).

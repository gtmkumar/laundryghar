---
name: project-core-consolidation
description: laundryghar.Core (port 5050) consolidation of Identity+Engagement+Mcp — JWT two-scheme decision, composite policy provider, namespace-collision handling
metadata:
  type: project
---

laundryghar.Core (:5050) absorbs Identity (:5050), Engagement (:5007), Mcp (:5009) as part of the 11→3 backend consolidation. Sources live under laundryghar.Core/{Identity,Engagement,Mcp}/ with ORIGINAL namespaces kept (no type renames). New merged Program.cs/csproj/appsettings/_Imports authored; old husk Program.cs/csproj/_Imports left in place.

**Why two JWT schemes (not one):** All three former services validate the SAME RS256 tokens (issuer=laundryghar-identity, audience=laundryghar-services). Identity issues + validates in-process; Engagement/Mcp validated via JWKS-over-HTTP. In one process the in-process signing key (keyProvider.SigningKey) is authoritative, so BOTH schemes use IssuerSigningKey directly — NO JWKS HTTP self-call. Default "Bearer" scheme (plain 401) serves Identity+Engagement; named "mcp" scheme adds the RFC 9728 OnChallenge (WWW-Authenticate: resource_metadata) and is bound ONLY to /mcp. A single scheme would have leaked the MCP discovery challenge onto Identity/Engagement 401s, changing their response shape.

**How to apply:** If editing JWT setup, keep both schemes' validation params identical and in-process. Never point Core at its own JWKS over HTTP. The /mcp route binds AuthenticationSchemes="mcp" + Policy="McpCustomerOnly".

**Composite policy provider (CoreAuthorizationPolicyProvider):** ASP.NET allows ONE IAuthorizationPolicyProvider but all three shipped their own with overlapping names. The merged provider maps: permission:* / CustomerOnly → Identity's requirements (Identity's permission: is a superset supporting pipe-OR, serves Engagement too); McpCustomerOnly → Mcp's CustomerOnlyRequirement (customer_mcp+scope OR customer). Mcp's internal "CustomerOnly" was renamed to "McpCustomerOnly" (wiring only, no HTTP contract change) so its broader token rule stays isolated from Identity's strict token_use=customer. Both CustomerOnlyHandler copies registered — type dispatch (AuthorizationHandler<TRequirement>) keeps them separate.

**Namespace-collision handling:** global usings are assembly-wide, so importing both Identity+Engagement Infrastructure.Auth/Services made JwtSettings/ICurrentUser/etc. ambiguous. Resolution: merged _Imports globally imports ONLY Identity's Infrastructure.Auth+Services; the 15 Engagement Application/Endpoint files that use Engagement's ICurrentUser got an explicit `using ...Engagement.Infrastructure.Services;` + `using ICurrentUser = ...Engagement....ICurrentUser;` alias (Engagement's ICurrentUser adds RequireBrandId, so it CANNOT be merged into Identity's). Mcp's TokenForwardingHandler + LaundryTools got explicit per-file usings. Program.cs references Engagement/Mcp Auth+config types fully qualified. Both ICurrentUser impls registered concretely in DI (Identity's as ICurrentTenant+its ICurrentUser; Engagement's as its own ICurrentUser).

**DownstreamServices default = http://localhost:5002** (Catalog+Orders merging into laundryghar.Operations:5002). OAuthResource McpBaseUrl/IdentityBaseUrl both default to :5050 (same process now).

**Deferred:** AppHost still references Projects.laundryghar_Identity/_Engagement/_Mcp on 3 ports — orchestrator must repoint to laundryghar_Core:5050 and drop the 3 old project resources. Old husk projects still in laundryghar.slnx. Core not yet added to slnx. Build verified standalone: `dotnet build laundryghar.Core/laundryghar.Core.csproj` = 0 errors (1 benign NU1510 warning on Microsoft.Extensions.Diagnostics.HealthChecks, kept for parity).

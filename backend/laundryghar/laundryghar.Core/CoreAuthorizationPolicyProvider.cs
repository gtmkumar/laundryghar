using Microsoft.Extensions.Options;

namespace laundryghar.Core;

/// <summary>
/// Single merged <see cref="IAuthorizationPolicyProvider"/> for the consolidated Core service.
///
/// ASP.NET Core permits exactly one registered policy provider, but the three absorbed
/// services each shipped their own (Identity's <c>PermissionPolicyProvider</c>, Engagement's
/// <c>PermissionPolicyProvider</c>, Mcp's <c>CustomerOnlyPolicyProvider</c>). Their policy-name
/// surfaces overlap (<c>permission:*</c>, <c>CustomerOnly</c>) but the underlying requirement
/// TYPES differ between Identity/Engagement and Mcp. This composite resolves every name to a
/// single canonical requirement so the correct (type-dispatched) handler runs:
///
///   • <c>permission:&lt;code&gt;</c> / <c>permission:&lt;a&gt;|&lt;b&gt;</c>
///       → Identity's <see cref="laundryghar.Identity.Infrastructure.Auth.PermissionRequirement"/>
///         / <see cref="laundryghar.Identity.Infrastructure.Auth.AnyPermissionRequirement"/>.
///         Identity's variant is a strict superset of Engagement's (it adds the pipe-separated
///         any-permission OR set), so it correctly serves Engagement's admin endpoints too.
///   • <c>CustomerOnly</c>
///       → Identity's <see cref="laundryghar.Identity.Infrastructure.Auth.CustomerOnlyRequirement"/>
///         (token_use=customer). Used by Identity's customer-auth endpoints. Identity's and
///         Engagement's CustomerOnly semantics are identical (exact token_use=customer match).
///   • <c>McpCustomerOnly</c>  (renamed from Mcp's internal "CustomerOnly" — wiring only, no HTTP
///         contract change)
///       → Mcp's <see cref="laundryghar.Mcp.Infrastructure.Auth.CustomerOnlyRequirement"/>
///         (token_use=customer_mcp + mcp:booking scope, OR token_use=customer). The MCP endpoint
///         binds this name so its broader acceptance rule is preserved exactly.
/// </summary>
public sealed class CoreAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PermissionPrefix      = "permission:";
    public const string CustomerOnlyPolicy    = "CustomerOnly";
    public const string McpCustomerOnlyPolicy = "McpCustomerOnly";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public CoreAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // ── Permission-gated admin policy (single code or pipe-separated OR set) ──
        // Mirrors Identity.PermissionPolicyProvider exactly.
        if (policyName.StartsWith(PermissionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = policyName[PermissionPrefix.Length..];
            var codes = remainder.Split('|', StringSplitOptions.RemoveEmptyEntries);

            AuthorizationPolicy policy;
            if (codes.Length == 1)
            {
                policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(
                        new laundryghar.Identity.Infrastructure.Auth.PermissionRequirement(codes[0]))
                    .Build();
            }
            else
            {
                policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(
                        new laundryghar.Identity.Infrastructure.Auth.AnyPermissionRequirement(codes))
                    .Build();
            }

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // ── CustomerOnly (Identity/Engagement semantics: token_use=customer) ──
        if (string.Equals(policyName, CustomerOnlyPolicy, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new laundryghar.Identity.Infrastructure.Auth.CustomerOnlyRequirement())
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // ── McpCustomerOnly (Mcp semantics: customer_mcp+scope OR customer) ──
        if (string.Equals(policyName, McpCustomerOnlyPolicy, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new laundryghar.Mcp.Infrastructure.Auth.CustomerOnlyRequirement())
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}

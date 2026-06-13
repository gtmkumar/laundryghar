using Microsoft.Extensions.Options;

// Requirement types are aliased explicitly: Orders and Logistics both define PermissionRequirement,
// so an unqualified reference would be ambiguous. We source permission / any-permission / customer-only
// from Orders and rider-only from Logistics (all behaviourally identical to their siblings).
using PermissionRequirement    = laundryghar.Orders.Infrastructure.Auth.PermissionRequirement;
using AnyPermissionRequirement = laundryghar.Orders.Infrastructure.Auth.AnyPermissionRequirement;
using CustomerOnlyRequirement  = laundryghar.Orders.Infrastructure.Auth.CustomerOnlyRequirement;
using RiderOnlyRequirement     = laundryghar.Logistics.Infrastructure.Auth.RiderOnlyRequirement;

namespace laundryghar.Operations;

/// <summary>
/// Merged authorization policy provider for the consolidated Operations service.
/// Replaces the four per-domain <c>PermissionPolicyProvider</c> classes (Catalog, Orders,
/// Warehouse, Logistics), which were functionally a subset of this union. ASP.NET resolves a
/// single <see cref="IAuthorizationPolicyProvider"/>, so exactly one must be registered.
///
/// Recognised policy names (verbatim, unchanged from the source services):
/// - "permission:&lt;code&gt;"         → token_use=user + the named permission (or platform_admin bypass).
/// - "permission:&lt;a&gt;|&lt;b&gt;[|...]"  → any-permission OR: caller must hold at least one code (R3-SEC-2).
/// - "CustomerOnly"               → token_use=customer  (Catalog + Orders customer-facing routes).
/// - "RiderOnly"                  → token_use=user AND user_type=rider (Logistics /api/v1/rider/* lane).
///
/// Requirement + handler types are sourced from the Orders namespace (permission / any-permission /
/// customer-only) and the Logistics namespace (rider-only). The corresponding handlers
/// (<c>Orders.PermissionHandler</c>, <c>Orders.AnyPermissionHandler</c>, <c>Orders.CustomerOnlyHandler</c>,
/// <c>Logistics.RiderOnlyHandler</c>) are registered in Program.cs and are byte-for-byte equivalent in
/// behaviour to their Catalog/Warehouse siblings, so route authorization is preserved exactly.
/// </summary>
public sealed class OperationsPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix       = "permission:";
    public const string CustomerOnlyPolicy = "CustomerOnly";
    public const string RiderOnlyPolicy    = "RiderOnly";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public OperationsPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()   => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Permission-gated admin policy (single code, or pipe-delimited any-of set).
        if (policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = policyName[PolicyPrefix.Length..];
            var codes = remainder.Split('|', StringSplitOptions.RemoveEmptyEntries);

            AuthorizationPolicy policy;
            if (codes.Length == 1)
            {
                policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(codes[0]))
                    .Build();
            }
            else
            {
                // Any one of the pipe-delimited codes satisfies this policy.
                policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AnyPermissionRequirement(codes))
                    .Build();
            }

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // Customer-only policy — token_use must be "customer".
        if (string.Equals(policyName, CustomerOnlyPolicy, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new CustomerOnlyRequirement())
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // Rider self-service policy — token_use=user AND user_type=rider.
        if (string.Equals(policyName, RiderOnlyPolicy, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new RiderOnlyRequirement())
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}

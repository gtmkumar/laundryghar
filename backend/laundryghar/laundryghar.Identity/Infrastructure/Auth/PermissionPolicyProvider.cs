using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Infrastructure.Auth;

/// <summary>
/// Dynamically creates authorization policies.
/// - "permission:&lt;code&gt;"          → requires token_use=user + the named permission (or platform_admin).
/// - "permission:&lt;a&gt;|&lt;b&gt;[|&lt;c&gt;...]"  → any-permission OR: caller must hold at least one of the
///                                    listed codes. Supports POS/Orders shared-route scenarios
///                                    (R3-SEC-2) where two independent permission families gate the
///                                    same endpoint (e.g. "permission:orders.create|pos.order.create").
/// - "CustomerOnly"                  → requires token_use=customer.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix       = "permission:";
    public const string CustomerOnlyPolicy = "CustomerOnly";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Permission-gated admin policy (single code or pipe-separated OR set)
        if (policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = policyName[PolicyPrefix.Length..];
            var codes = remainder.Split('|', StringSplitOptions.RemoveEmptyEntries);

            AuthorizationPolicy policy;
            if (codes.Length == 1)
            {
                // Fast path: single permission code
                policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(codes[0]))
                    .Build();
            }
            else
            {
                // Any-permission OR: caller satisfies the policy if they hold any one code.
                // Implemented as a single AnyPermissionRequirement so the PermissionHandler
                // never needs to know the multi-code case — a dedicated handler resolves it.
                policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AnyPermissionRequirement(codes))
                    .Build();
            }

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // Customer-only policy — token_use must be "customer"
        if (string.Equals(policyName, CustomerOnlyPolicy, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new CustomerOnlyRequirement())
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}

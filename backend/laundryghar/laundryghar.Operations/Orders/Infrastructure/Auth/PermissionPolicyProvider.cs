using Microsoft.Extensions.Options;

namespace laundryghar.Orders.Infrastructure.Auth;

/// <summary>
/// Dynamically creates authorization policies for the Orders service.
/// - "permission:&lt;code&gt;"         → requires token_use=user + the named permission (or platform_admin).
/// - "permission:&lt;a&gt;|&lt;b&gt;[|...]"  → any-permission OR (R3-SEC-2): caller must hold at least one code.
///                                   Used on shared order routes to accept both orders.* and pos.order.* families.
/// - "CustomerOnly"               → requires token_use=customer.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix       = "permission:";
    public const string CustomerOnlyPolicy = "CustomerOnly";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()  => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
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

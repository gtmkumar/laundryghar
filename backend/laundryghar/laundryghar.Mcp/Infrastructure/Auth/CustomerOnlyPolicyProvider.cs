using Microsoft.Extensions.Options;

namespace laundryghar.Mcp.Infrastructure.Auth;

/// <summary>
/// Provides the "CustomerOnly" authorization policy.
/// The MCP service is customer-facing only — no admin permission policies needed here.
/// Mirrors the PermissionPolicyProvider pattern from Catalog but stripped to just CustomerOnly.
/// </summary>
public sealed class CustomerOnlyPolicyProvider : IAuthorizationPolicyProvider
{
    public const string CustomerOnlyPolicy = "CustomerOnly";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public CustomerOnlyPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
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

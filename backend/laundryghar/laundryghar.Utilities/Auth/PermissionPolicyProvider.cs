using laundryghar.Utilities.Authorization.Abac;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace laundryghar.Utilities.Auth;

/// <summary>
/// Dynamically creates authorization policies.
/// - "permission:&lt;code&gt;"          → requires token_use=user + the named permission (or platform_admin).
/// - "permission:&lt;a&gt;|&lt;b&gt;[|&lt;c&gt;...]"  → any-permission OR: caller must hold at least one of the
///                                    listed codes. Supports POS/Orders shared-route scenarios
///                                    (R3-SEC-2) where two independent permission families gate the
///                                    same endpoint (e.g. "permission:orders.create|pos.order.create").
/// - "apiscope:&lt;scope&gt;"             → requires an API KEY (token_use=api_key) issued that scope.
/// - "CustomerOnly"                  → requires token_use=customer.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix        = "permission:";
    public const string CustomerOnlyPolicy  = "CustomerOnly";
    public const string RiderOnlyPolicy     = "RiderOnly";
    public const string PartnerOnlyPolicy   = "PartnerOnly";
    public const string PartnerAdminPolicy  = "PartnerAdmin";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // "apiscope:<scope>" — the machine-credential equivalent (§11 P4). Checked FIRST because it
        // is unambiguous and because these policies bind to the ApiKey scheme rather than the
        // default one: a signed-in human must never satisfy an API-key scope, and vice versa.
        if (ApiKey.ApiScopePolicy.TryBuild(policyName, out var scopePolicy))
            return Task.FromResult(scopePolicy);

        // Permission-gated admin policy (single code or pipe-separated OR set)
        if (policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = policyName[PolicyPrefix.Length..];
            var codes = remainder.Split('|', StringSplitOptions.RemoveEmptyEntries);

            // The ABAC requirement rides alongside the permission requirement on every permission
            // policy (A4.2). ASP.NET Core demands that EVERY requirement be satisfied, so the
            // meaning becomes "hold the code AND survive the policy rows" — the coarse RBAC gate
            // stays first and stays authoritative for what a role can enumerate.
            //
            // Adding it here rather than per-endpoint is what makes the shadow window worth
            // anything: every permission-gated endpoint reports a decision from day one, so the
            // parity diff (A6.2) sees the whole surface rather than the handful someone remembered
            // to tag. AbacAuthorizationHandler succeeds immediately when the engine is disabled or
            // the endpoint declares no resource, so this is inert until a module is cut over.
            var abac = new AbacRequirement();

            AuthorizationPolicy policy;
            if (codes.Length == 1)
            {
                // Fast path: single permission code
                policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(codes[0]), abac)
                    .Build();
            }
            else
            {
                // Any-permission OR: caller satisfies the policy if they hold any one code.
                // Implemented as a single AnyPermissionRequirement so the PermissionHandler
                // never needs to know the multi-code case — a dedicated handler resolves it.
                policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AnyPermissionRequirement(codes), abac)
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

        // Rider-only policy — token_use=user AND user_type=rider (rider self-service lane)
        if (string.Equals(policyName, RiderOnlyPolicy, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new RiderOnlyRequirement())
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // Partner-only policy — token_use must be "partner" (RaaS partner lane)
        if (string.Equals(policyName, PartnerOnlyPolicy, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PartnerOnlyRequirement())
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // Partner-admin policy — token_use=partner AND partner_role=partner_admin (partner-admin lane)
        if (string.Equals(policyName, PartnerAdminPolicy, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PartnerAdminRequirement())
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}

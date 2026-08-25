using System.Security.Claims;
using laundryghar.Utilities.Auth.ApiKey;
using Microsoft.AspNetCore.Authorization;

namespace laundryghar.Utilities.Auth.ApiKey;

/// <summary>Requires an API key carrying a specific scope. Policy name: <c>apiscope:orders.read</c>.</summary>
public sealed class ApiScopeRequirement : IAuthorizationRequirement
{
    public ApiScopeRequirement(string scope) => Scope = scope;
    public string Scope { get; }
}

/// <summary>
/// Grants only when the caller is an API key that was ISSUED the scope in question.
///
/// <para>Two rules, both deliberately strict:</para>
/// <list type="number">
/// <item>A signed-in human never satisfies an API-key policy. They have their own permission system;
/// letting a session token through here would mean the public API's scopes are advisory.</item>
/// <item>Scopes are exact matches, with one wildcard: a key holding <c>*</c> holds everything. There
/// is no prefix matching, because <c>orders.read</c> silently granting <c>orders.readwrite</c> is the
/// kind of bug nobody finds until it matters.</item>
/// </list>
/// </summary>
public sealed class ApiScopeHandler : AuthorizationHandler<ApiScopeRequirement>
{
    /// <summary>The scope that means "everything". Issued deliberately or not at all.</summary>
    public const string Wildcard = "*";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ApiScopeRequirement requirement)
    {
        var principal = context.User;
        if (principal?.Identity?.IsAuthenticated != true) return Task.CompletedTask;

        if (principal.FindFirst("token_use")?.Value != ApiKeyClaims.TokenUse)
            return Task.CompletedTask;

        var held = principal.FindAll(ApiKeyClaims.ScopeClaim).Select(c => c.Value).ToList();
        if (held.Contains(Wildcard, StringComparer.Ordinal)
            || held.Contains(requirement.Scope, StringComparer.Ordinal))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>Turns <c>apiscope:&lt;scope&gt;</c> policy names into <see cref="ApiScopeRequirement"/>s,
/// so a new scope needs no registration — the same convention as <c>permission:&lt;code&gt;</c>.</summary>
public static class ApiScopePolicy
{
    public const string Prefix = "apiscope:";

    public static bool TryBuild(string policyName, out AuthorizationPolicy? policy)
    {
        policy = null;
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var scope = policyName[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(scope)) return false;

        policy = new AuthorizationPolicyBuilder(ApiKeyClaims.Scheme)
            .AddRequirements(new ApiScopeRequirement(scope))
            .Build();
        return true;
    }
}

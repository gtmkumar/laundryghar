namespace laundryghar.Identity.Infrastructure.Auth;

/// <summary>
/// Authorization requirement that succeeds when the caller holds ANY ONE of the listed
/// permission codes. Used by pipe-syntax policies (e.g. "permission:orders.create|pos.order.create")
/// to gate shared endpoints that two independent role families must reach.
///
/// Platform admins bypass this requirement exactly as they do PermissionRequirement.
/// </summary>
public sealed class AnyPermissionRequirement : IAuthorizationRequirement
{
    /// <summary>At least one of these codes must be present in the caller's permissions claim.</summary>
    public IReadOnlyList<string> PermissionCodes { get; }

    public AnyPermissionRequirement(IEnumerable<string> codes)
    {
        var list = codes.ToList();
        if (list.Count == 0) throw new ArgumentException("At least one permission code is required.", nameof(codes));
        PermissionCodes = list;
    }
}

/// <summary>
/// Evaluates <see cref="AnyPermissionRequirement"/> against the JWT permissions claim.
/// Mirrors the logic in <see cref="PermissionHandler"/> — token_use=user required,
/// platform_admin bypasses, any matching code in the space-separated claim satisfies.
/// </summary>
public sealed class AnyPermissionHandler : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyPermissionRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (!string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal))
            return Task.CompletedTask; // leave unsatisfied

        var userType = context.User.FindFirstValue("user_type");
        if (userType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var permsClaim = context.User.FindFirstValue("permissions");
        if (!string.IsNullOrEmpty(permsClaim))
        {
            var perms = permsClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requirement.PermissionCodes.Any(code =>
                    perms.Contains(code, StringComparer.OrdinalIgnoreCase)))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}

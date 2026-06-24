using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace laundryghar.Utilities.Auth;

/// <summary>
/// Evaluates PermissionRequirement by checking the "permissions" claim in the JWT.
/// Implicitly requires token_use=user — customer tokens carry no permissions and must
/// never satisfy admin permission policies even if they happen to be authenticated.
/// Platform admins (user_type=platform_admin) bypass individual permission checks.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Gate 1: token_use must be "user". Customer tokens (token_use=customer) are
        // silently rejected here — they will not satisfy any permission-based policy.
        var tokenUse = context.User.FindFirstValue("token_use");
        if (!string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal))
            return Task.CompletedTask; // leave requirement unsatisfied

        var userType = context.User.FindFirstValue("user_type");

        // Platform admins bypass individual permission checks
        if (userType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var permsClaim = context.User.FindFirstValue("permissions");
        if (!string.IsNullOrEmpty(permsClaim))
        {
            // Canonicalize both sides so renamed permissions resolve across the rename
            // (legacy claims in already-issued JWTs still satisfy the new code). See PermissionAlias.
            var required = PermissionAlias.Canonical(requirement.PermissionCode);
            var perms = permsClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(PermissionAlias.Canonical);
            if (perms.Contains(required, StringComparer.OrdinalIgnoreCase))
                context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

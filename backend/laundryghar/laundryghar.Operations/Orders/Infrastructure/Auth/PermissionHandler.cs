namespace laundryghar.Orders.Infrastructure.Auth;

/// <summary>
/// Evaluates PermissionRequirement from JWT permissions claim.
/// Gate 1: token_use must be "user" — customers never satisfy permission policies.
/// Gate 2: platform_admin bypasses individual checks.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (!string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal))
            return Task.CompletedTask;

        var userType = context.User.FindFirstValue("user_type");
        if (userType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var perms = context.User.FindFirstValue("permissions");
        if (!string.IsNullOrEmpty(perms))
        {
            if (perms.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Contains(requirement.PermissionCode, StringComparer.OrdinalIgnoreCase))
                context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
